namespace EVChargeSwap.Features.Locations.Caching;

using System.Diagnostics.Metrics;
using System.Text.Json;
using EVChargeSwap.Common.Pagination;
using EVChargeSwap.Features.Locations.Responses;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

public sealed class RedisLocationCache(
    IConnectionMultiplexer? redis,
    ILogger<RedisLocationCache> logger) : ILocationCache
{
    private const string KeyPrefix = "evcs:locations";
    private const string VersionKey = KeyPrefix + ":version";
    private const int MaxCachedPage = 50;

    private static readonly TimeSpan LocationTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PagedTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Meter Meter = new("EVChargeSwap.RedisCache");
    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("redis.location.cache.hits");
    private static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("redis.location.cache.misses");
    private static readonly Counter<long> CacheFallbacks = Meter.CreateCounter<long>("redis.location.cache.fallbacks");

    // Atomic compare-version-and-set to prevent TOCTOU cache poisoning during concurrent invalidations.
    private const string ConditionalSetScript = """
        local current = redis.call('GET', KEYS[1]) or '0'
        if current == ARGV[1] then
            redis.call('SET', KEYS[2], ARGV[2], 'PX', ARGV[3])
            return 1
        end
        return 0
        """;

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
            CacheFallbacks.Add(1);
            logger.LogWarning(ex, "Failed to acquire Redis database instance. Falling back to primary database.");
            return null;
        }
    }

    public async Task<long> GetVersionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = GetDatabase();
        if (db is null) return 0;

        try
        {
            return await GetCurrentVersionAsync(db);
        }
        catch (Exception ex)
        {
            CacheFallbacks.Add(1);
            logger.LogWarning(ex, "Redis version read failed. Degrading to database.");
            return 0;
        }
    }

    public async Task<LocationResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = GetDatabase();
        if (db is null) return null;

        try
        {
            var version = await GetCurrentVersionAsync(db);
            var value = await db.StringGetAsync(LocationKey(version, id));
            if (value.IsNullOrEmpty)
            {
                CacheMisses.Add(1);
                return null;
            }

            CacheHits.Add(1);
            return JsonSerializer.Deserialize<LocationResponse>(value!, JsonOptions);
        }
        catch (Exception ex)
        {
            CacheFallbacks.Add(1);
            logger.LogWarning(ex, "Redis GET failed for location {LocationId}. Degrading to database.", id);
            return null;
        }
    }

    public async Task SetAsync(LocationResponse location, CancellationToken cancellationToken)
    {
        var version = await GetVersionAsync(cancellationToken);
        await SetAsync(location, version, cancellationToken);
    }

    public async Task SetAsync(LocationResponse location, long expectedVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = GetDatabase();
        if (db is null) return;

        try
        {
            var json = JsonSerializer.Serialize(location, JsonOptions);
            await db.ScriptEvaluateAsync(
                ConditionalSetScript,
                [(RedisKey)VersionKey, (RedisKey)LocationKey(expectedVersion, location.Id)],
                [expectedVersion.ToString(), json, (long)LocationTtl.TotalMilliseconds]);
        }
        catch (Exception ex)
        {
            CacheFallbacks.Add(1);
            logger.LogWarning(ex, "Redis SET failed for location {LocationId}.", location.Id);
        }
    }

    public async Task<PagedResponse<LocationResponse>?> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (page > MaxCachedPage) return null;

        var db = GetDatabase();
        if (db is null) return null;

        try
        {
            var version = await GetCurrentVersionAsync(db);
            var value = await db.StringGetAsync(PagedKey(version, page, pageSize));
            if (value.IsNullOrEmpty)
            {
                CacheMisses.Add(1);
                return null;
            }

            CacheHits.Add(1);
            return JsonSerializer.Deserialize<PagedResponse<LocationResponse>>(value!, JsonOptions);
        }
        catch (Exception ex)
        {
            CacheFallbacks.Add(1);
            logger.LogWarning(ex, "Redis GET failed for paged locations page {Page}.", page);
            return null;
        }
    }

    public async Task SetPagedAsync(PagedResponse<LocationResponse> pagedResponse, CancellationToken cancellationToken)
    {
        var version = await GetVersionAsync(cancellationToken);
        await SetPagedAsync(pagedResponse, version, cancellationToken);
    }

    public async Task SetPagedAsync(PagedResponse<LocationResponse> pagedResponse, long expectedVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Avoid Redis key spray on out-of-range or deep pages.
        if (pagedResponse.Page > MaxCachedPage || (pagedResponse.Items.Count == 0 && pagedResponse.TotalCount > 0))
            return;

        var db = GetDatabase();
        if (db is null) return;

        try
        {
            var json = JsonSerializer.Serialize(pagedResponse, JsonOptions);
            await db.ScriptEvaluateAsync(
                ConditionalSetScript,
                [(RedisKey)VersionKey, (RedisKey)PagedKey(expectedVersion, pagedResponse.Page, pagedResponse.PageSize)],
                [expectedVersion.ToString(), json, (long)PagedTtl.TotalMilliseconds]);
        }
        catch (Exception ex)
        {
            CacheFallbacks.Add(1);
            logger.LogWarning(ex, "Redis SET failed for paged locations page {Page}.", pagedResponse.Page);
        }
    }

    public async Task InvalidateLocationAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = GetDatabase();
        if (db is null) return;

        try
        {
            // Bumping the version invalidates both paged listings and versioned single-location entries atomically.
            await db.StringIncrementAsync(VersionKey);
        }
        catch (Exception ex)
        {
            CacheFallbacks.Add(1);
            logger.LogWarning(ex, "Redis invalidation failed for location {LocationId}.", id);
        }
    }

    public async Task InvalidateAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = GetDatabase();
        if (db is null) return;

        try
        {
            await db.StringIncrementAsync(VersionKey);
        }
        catch (Exception ex)
        {
            CacheFallbacks.Add(1);
            logger.LogWarning(ex, "Redis InvalidateAll failed.");
        }
    }

    private static string LocationKey(long version, Guid id) => $"{KeyPrefix}:v{version}:{id:D}";
    private static string PagedKey(long version, int page, int pageSize) => $"{KeyPrefix}:v{version}:paged:{page}:{pageSize}";

    private static async Task<long> GetCurrentVersionAsync(IDatabase db)
    {
        var val = await db.StringGetAsync(VersionKey);
        if (val.TryParse(out long ver))
        {
            return ver;
        }

        // Default to 0 so the very first INCR advances 0 -> 1 without colliding with cold-start entries.
        return 0;
    }
}
