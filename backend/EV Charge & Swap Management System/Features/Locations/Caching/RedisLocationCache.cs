namespace EVChargeSwap.Features.Locations.Caching;

using System.Text.Json;
using EVChargeSwap.Common.Pagination;
using EVChargeSwap.Features.Locations.Responses;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

public sealed class RedisLocationCache(
    IConnectionMultiplexer? redis,
    ILogger<RedisLocationCache> logger) : ILocationCache
{
    private static readonly TimeSpan LocationTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PagedTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private IDatabase? GetDatabase()
    {
        if (redis is null || !redis.IsConnected)
        {
            return null;
        }

        try
        {
            return redis.GetDatabase();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to acquire Redis database instance. Falling back to primary database.");
            return null;
        }
    }

    public async Task<LocationResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var db = GetDatabase();
        if (db is null) return null;

        try
        {
            var value = await db.StringGetAsync($"locations:{id}");
            if (value.IsNullOrEmpty) return null;

            return JsonSerializer.Deserialize<LocationResponse>(value!, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis GET failed for location {LocationId}. Degrading to database.", id);
            return null;
        }
    }

    public async Task SetAsync(LocationResponse location, CancellationToken cancellationToken)
    {
        var db = GetDatabase();
        if (db is null) return;

        try
        {
            var json = JsonSerializer.Serialize(location, JsonOptions);
            await db.StringSetAsync($"locations:{location.Id}", json, LocationTtl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis SET failed for location {LocationId}.", location.Id);
        }
    }

    public async Task<PagedResponse<LocationResponse>?> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var db = GetDatabase();
        if (db is null) return null;

        try
        {
            var version = await GetCurrentVersionAsync(db);
            var value = await db.StringGetAsync($"locations:v{version}:paged:{page}:{pageSize}");
            if (value.IsNullOrEmpty) return null;

            return JsonSerializer.Deserialize<PagedResponse<LocationResponse>>(value!, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis GET failed for paged locations page {Page}.", page);
            return null;
        }
    }

    public async Task SetPagedAsync(PagedResponse<LocationResponse> pagedResponse, CancellationToken cancellationToken)
    {
        var db = GetDatabase();
        if (db is null) return;

        try
        {
            var version = await GetCurrentVersionAsync(db);
            var json = JsonSerializer.Serialize(pagedResponse, JsonOptions);
            await db.StringSetAsync(
                $"locations:v{version}:paged:{pagedResponse.Page}:{pagedResponse.PageSize}",
                json,
                PagedTtl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis SET failed for paged locations page {Page}.", pagedResponse.Page);
        }
    }

    public async Task InvalidateLocationAsync(Guid id, CancellationToken cancellationToken)
    {
        var db = GetDatabase();
        if (db is null) return;

        try
        {
            await Task.WhenAll(
                db.KeyDeleteAsync($"locations:{id}"),
                db.StringIncrementAsync("locations:version")
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis invalidation failed for location {LocationId}.", id);
        }
    }

    public async Task InvalidateAllAsync(CancellationToken cancellationToken)
    {
        var db = GetDatabase();
        if (db is null) return;

        try
        {
            await db.StringIncrementAsync("locations:version");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis InvalidateAll failed.");
        }
    }

    private static async Task<long> GetCurrentVersionAsync(IDatabase db)
    {
        var val = await db.StringGetAsync("locations:version");
        if (val.TryParse(out long ver))
        {
            return ver;
        }

        return 1;
    }
}
