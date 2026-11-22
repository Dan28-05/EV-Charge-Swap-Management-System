using System.Text.Json;
using System.Text.RegularExpressions;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Options;
using HttpMethod = Elastic.Transport.HttpMethod;

namespace EVChargeSwap.Features.Locations.Search;

public sealed class ElasticLocationIndex(IOptions<LocationSearchOptions> options) : IDisposable
{
    internal const int ResultWindow = 10_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly RequestConfiguration SearchRequest = new() { RequestTimeout = TimeSpan.FromSeconds(1) };
    private readonly ElasticsearchClient? _client = CreateClient(options.Value);

    public bool Enabled => _client is not null;
    private string Alias => options.Value.Alias;

    public static bool IsValidAlias(string? alias) =>
        alias is not null && Regex.IsMatch(alias, @"\A[a-z][a-z0-9-]{0,62}\z", RegexOptions.CultureInvariant);

    internal async Task<LocationSearchPage> SearchAsync(string term, int offset, int size, CancellationToken ct)
    {
        var query = new
        {
            from = offset,
            size,
            track_total_hits = true,
            _source = false,
            sort = new[] { "_score", "id" },
            query = new
            {
                dis_max = new
                {
                    queries = new[]
                    {
                        PrefixQuery(term, "name", 4),
                        PrefixQuery(term, "address", 1)
                    }
                }
            }
        };
        var response = await SendAsync(HttpMethod.POST, $"/{Alias}/_search?allow_partial_search_results=false",
            Serialize(query), ct, SearchRequest);
        EnsureSuccess(response, "search");

        using var json = ParseBody(response.Body, "search");
        try
        {
            if (json.RootElement.GetProperty("timed_out").GetBoolean())
                throw new LocationIndexException("search timeout", null);

            var hits = json.RootElement.GetProperty("hits");
            var ids = hits.GetProperty("hits").EnumerateArray()
                .Select(hit => Guid.Parse(hit.GetProperty("_id").GetString()!)).ToArray();
            return new LocationSearchPage(ids, hits.GetProperty("total").GetProperty("value").GetInt32());
        }
        catch (LocationIndexException) { throw; }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or ArgumentNullException)
        {
            throw new LocationIndexException("parse search response", response.ApiCallDetails.HttpStatusCode, ex);
        }
    }

    private static object PrefixQuery(string term, string field, int boost) => new
    {
        multi_match = new
        {
            query = term,
            type = "bool_prefix",
            @operator = "and",
            fields = new[] { field, field + "._2gram", field + "._3gram" },
            boost
        }
    };

    public async Task<IReadOnlyList<string>> GetAliasTargetsAsync(CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.GET, $"/_alias/{Alias}", null, ct);
        if (response.ApiCallDetails.HttpStatusCode == 404)
            return [];

        EnsureSuccess(response, "read alias");
        using var json = ParseBody(response.Body, "read alias");
        return json.RootElement.EnumerateObject().Select(entry => entry.Name).ToArray();
    }

    public async Task<string> CreateIndexAsync(CancellationToken ct)
    {
        var index = $"{Alias}-v1-{Guid.NewGuid():N}";
        EnsureSuccess(await SendAsync(HttpMethod.PUT, $"/{index}", PostData.String(LocationSearchMapping.Definition), ct),
            "create index");
        return index;
    }

    public async Task TryDeleteIndexAsync(string index, CancellationToken ct)
    {
        try
        {
            await SendAsync(HttpMethod.DELETE, $"/{index}", null, ct);
        }
        catch
        {
            // Best-effort cleanup of orphan staging index.
        }
    }

    internal async Task BulkIndexAsync(string index, IReadOnlyList<LocationSearchDocument> documents, CancellationToken ct)
    {
        try
        {
            var response = await _client!.IndexManyAsync(documents, index, ct);
            if (!response.ApiCallDetails.HasSuccessfulStatusCode)
                throw new LocationIndexException("bulk index", response.ApiCallDetails.HttpStatusCode);
            if (response.Errors)
                throw new LocationIndexException("bulk index contained failed items", response.ItemsWithErrors.First().Status);
        }
        catch (LocationIndexException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new LocationIndexException("bulk index", null, ex);
        }
    }

    public async Task PublishIndexAsync(string index, IReadOnlyList<string> previous, CancellationToken ct)
    {
        EnsureSuccess(await SendAsync(HttpMethod.POST, $"/{index}/_refresh", null, ct), "refresh index");
        var actions = previous.Select(old => (object)new
        {
            remove = new { index = old, alias = Alias, must_exist = true }
        }).ToList();
        actions.Add(new { add = new { index, alias = Alias, is_write_index = true } });
        var response = await SendAsync(HttpMethod.POST, "/_aliases", Serialize(new { actions }), ct);
        EnsureSuccess(response, "switch alias");
        using var json = ParseBody(response.Body, "switch alias");
        if (!json.RootElement.GetProperty("acknowledged").GetBoolean() ||
            (json.RootElement.TryGetProperty("errors", out var errors) && errors.GetBoolean()))
            throw new LocationIndexException("switch alias was not acknowledged", null);
    }

    internal async Task UpsertAsync(LocationSearchDocument document, CancellationToken ct) =>
        EnsureSuccess(await SendAsync(HttpMethod.PUT, $"/{Alias}/_doc/{document.Id:D}?require_alias=true",
            Serialize(document), ct), "upsert station");

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.DELETE, $"/{Alias}/_doc/{id:D}?require_alias=true", null, ct);
        if (response.ApiCallDetails.HttpStatusCode == 404 && !string.IsNullOrWhiteSpace(response.Body))
        {
            try
            {
                using var json = JsonDocument.Parse(response.Body);
                if (json.RootElement.TryGetProperty("result", out var result) && result.GetString() == "not_found")
                    return;
            }
            catch (JsonException)
            {
                // Fall through to EnsureSuccess to signal non-Elasticsearch 404.
            }
        }
        EnsureSuccess(response, "remove station");
    }

    private async Task<StringResponse> SendAsync(HttpMethod method, string path, PostData? body, CancellationToken ct,
        RequestConfiguration? configuration = null)
    {
        try
        {
            return await _client!.Transport.RequestAsync<StringResponse>(new EndpointPath(method, path),
                body, null, configuration, cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new LocationIndexException($"{method} {path}", null, ex);
        }
    }

    private static JsonDocument ParseBody(string? body, string operation)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new LocationIndexException($"{operation} returned empty body", null);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new LocationIndexException($"{operation} returned invalid JSON", null, ex);
        }
    }

    private static PostData Serialize<T>(T payload) => PostData.String(JsonSerializer.Serialize(payload, Json));

    private static void EnsureSuccess(StringResponse response, string operation)
    {
        if (!response.ApiCallDetails.HasSuccessfulStatusCode)
            throw new LocationIndexException(operation, response.ApiCallDetails.HttpStatusCode);
    }

    private static ElasticsearchClient? CreateClient(LocationSearchOptions settings)
    {
        if (!settings.Enabled) return null;
        var clientSettings = new ElasticsearchClientSettings(new Uri(settings.Endpoint!))
            .RequestTimeout(TimeSpan.FromSeconds(10))
            .MaximumRetries(0);
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            clientSettings.Authentication(new ApiKey(settings.ApiKey));
        return new ElasticsearchClient(clientSettings);
    }

    public void Dispose()
    {
        if (_client?.Transport is IDisposable disposable)
            disposable.Dispose();
    }
}

internal sealed class LocationIndexException(string operation, int? statusCode, Exception? innerException = null)
    : Exception($"Elasticsearch operation '{operation}' failed (HTTP {statusCode?.ToString() ?? "unavailable"}).", innerException)
{
    public bool IsTransient => statusCode is null or 404 or 408 or 429 or >= 500;
}
