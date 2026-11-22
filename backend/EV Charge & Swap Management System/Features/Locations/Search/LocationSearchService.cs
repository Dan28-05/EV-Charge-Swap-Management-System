using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EVChargeSwap.Data;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Locations.Search;

public sealed partial class LocationSearchService(ElasticLocationIndex index, ILogger<LocationSearchService> logger)
{
    private static readonly Meter Meter = new("EVChargeSwap.LocationSearch");
    private static readonly Counter<long> Fallbacks = Meter.CreateCounter<long>("location.search.fallbacks");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("location.search.elastic.duration", "ms");

    internal async Task<LocationSearchPage?> TrySearchAsync(string term, int offset, int size, CancellationToken ct)
    {
        if (!index.Enabled || (long)offset + size > ElasticLocationIndex.ResultWindow) return null;

        var started = Stopwatch.GetTimestamp();
        try
        {
            return await index.SearchAsync(term.Trim().Normalize(), offset, size, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
            ex is LocationIndexException or JsonException or FormatException or KeyNotFoundException or OperationCanceledException)
        {
            Fallbacks.Add(1);
            logger.LogDebug("Station search is using PostgreSQL after an Elasticsearch {FailureType}.", ex.GetType().Name);
            return null;
        }
        finally
        {
            Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    internal static async Task<LocationSearchPage> SearchPostgresAsync(AppDbContext db, string term,
        int offset, int size, CancellationToken ct)
    {
        var tokens = Tokenize(term);
        if (tokens.Length == 0) return new([], 0);

        var matches = new List<(Guid Id, string Name, int Rank)>();
        var candidates = db.Locations.AsNoTracking().Where(location => location.IsActive)
            .Select(location => new { location.Id, location.Name, location.Address });
        await foreach (var location in candidates.AsAsyncEnumerable().WithCancellation(ct))
        {
            if (Matches(location.Name, tokens))
                matches.Add((location.Id, location.Name, 2));
            else if (Matches(location.Address, tokens))
                matches.Add((location.Id, location.Name, 1));
        }

        var ids = matches.OrderByDescending(x => x.Rank).ThenBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Id).Skip(offset).Take(size).Select(x => x.Id).ToArray();
        return new LocationSearchPage(ids, matches.Count);
    }

    private static bool Matches(string text, string[] query)
    {
        var words = Tokenize(text);
        return query.Take(query.Length - 1).All(token => words.Contains(token, StringComparer.Ordinal)) &&
            words.Any(word => word.StartsWith(query[^1], StringComparison.Ordinal));
    }

    private static string[] Tokenize(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var ch in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            folded.Append(ch == 'đ' ? 'd' : ch);
        }
        return WordPattern().Matches(folded.ToString()).Select(match => match.Value).ToArray();
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}
