namespace EVChargeSwap.Tests.Unit;

using EVChargeSwap.Common.Pagination;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Locations.Caching;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Locations.Requests;
using EVChargeSwap.Features.Locations.Responses;
using EVChargeSwap.Features.Locations.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

public sealed class LocationCacheTests
{
    [Fact]
    public async Task RedisLocationCache_WhenDisconnectedOrNull_ReturnsNullWithoutThrowing()
    {
        var cache = new RedisLocationCache(null, NullLogger<RedisLocationCache>.Instance);
        var id = Guid.NewGuid();

        var result = await cache.GetAsync(id, CancellationToken.None);
        Assert.Null(result);

        var pagedResult = await cache.GetPagedAsync(1, 20, CancellationToken.None);
        Assert.Null(pagedResult);

        // Setting or invalidating when null must not throw
        await cache.SetAsync(new LocationResponse(id, "Test", "Address", 10, 106, true, false, LocationOperatingStatus.Unknown), CancellationToken.None);
        await cache.InvalidateLocationAsync(id, CancellationToken.None);
        await cache.InvalidateAllAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LocationService_GetAsync_ReturnsCachedValue_WhenPresent()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var stationId = Guid.NewGuid();
        var cachedStation = new LocationResponse(stationId, "Cached Station", "Cached Address", 10.5m, 106.5m, true, true, LocationOperatingStatus.Unknown);

        var mockCache = new TestInMemoryLocationCache();
        await mockCache.SetAsync(cachedStation, CancellationToken.None);

        var service = new LocationService(db, mockCache);
        var result = await service.GetAsync(stationId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Cached Station", result.Name);
        Assert.Equal("Cached Address", result.Address);
    }

    [Fact]
    public async Task LocationService_GetAsync_FetchesFromDbAndPopulatesCache_OnMiss()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var entity = new Location("DB Station", "DB Address", 10.7m, 106.7m, true, false);
        db.Locations.Add(entity);
        await db.SaveChangesAsync();

        var mockCache = new TestInMemoryLocationCache();
        var service = new LocationService(db, mockCache);

        // First call: cache miss, reads from DB and populates cache
        var result = await service.GetAsync(entity.Id, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("DB Station", result.Name);

        // Verify it was populated in cache
        var fromCache = await mockCache.GetAsync(entity.Id, CancellationToken.None);
        Assert.NotNull(fromCache);
        Assert.Equal("DB Station", fromCache.Name);
    }

    [Fact]
    public async Task LocationService_UpdateAsync_InvalidatesCache()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var entity = new Location("Initial Station", "Address", 10.7m, 106.7m, true, false);
        db.Locations.Add(entity);
        await db.SaveChangesAsync();

        var mockCache = new TestInMemoryLocationCache();
        var service = new LocationService(db, mockCache);

        // Populate cache
        await service.GetAsync(entity.Id, CancellationToken.None);
        Assert.NotNull(await mockCache.GetAsync(entity.Id, CancellationToken.None));

        // Update location
        var updateRequest = new LocationUpsertRequest
        {
            Name = "Updated Station",
            Address = "New Address",
            Latitude = 10.8m,
            Longitude = 106.8m,
            OffersCarCharging = true,
            OffersBatterySwap = true
        };
        await service.UpdateAsync(entity.Id, updateRequest, CancellationToken.None);

        // Verify cached item was updated with new data
        var cached = await mockCache.GetAsync(entity.Id, CancellationToken.None);
        Assert.NotNull(cached);
        Assert.Equal("Updated Station", cached.Name);
    }

    [Fact]
    public async Task LocationService_SetActiveAsync_DeactivatingLocation_InvalidatesCache()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var entity = new Location("Active Station", "Address", 10.7m, 106.7m, true, false);
        db.Locations.Add(entity);
        await db.SaveChangesAsync();

        var mockCache = new TestInMemoryLocationCache();
        var service = new LocationService(db, mockCache);

        await service.GetAsync(entity.Id, CancellationToken.None);
        Assert.NotNull(await mockCache.GetAsync(entity.Id, CancellationToken.None));

        // Deactivate station
        await service.SetActiveAsync(entity.Id, false, CancellationToken.None);

        // Verify it was purged from cache
        var cached = await mockCache.GetAsync(entity.Id, CancellationToken.None);
        Assert.Null(cached);
    }

    private sealed class TestInMemoryLocationCache : ILocationCache
    {
        private readonly Dictionary<Guid, LocationResponse> _store = [];
        private readonly Dictionary<string, PagedResponse<LocationResponse>> _pagedStore = [];

        public Task<LocationResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_store.TryGetValue(id, out var val) ? val : null);

        public Task SetAsync(LocationResponse location, CancellationToken cancellationToken)
        {
            _store[location.Id] = location;
            return Task.CompletedTask;
        }

        public Task<PagedResponse<LocationResponse>?> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(_pagedStore.TryGetValue($"{page}:{pageSize}", out var val) ? val : null);

        public Task SetPagedAsync(PagedResponse<LocationResponse> pagedResponse, CancellationToken cancellationToken)
        {
            _pagedStore[$"{pagedResponse.Page}:{pagedResponse.PageSize}"] = pagedResponse;
            return Task.CompletedTask;
        }

        public Task InvalidateLocationAsync(Guid id, CancellationToken cancellationToken)
        {
            _store.Remove(id);
            _pagedStore.Clear();
            return Task.CompletedTask;
        }

        public Task InvalidateAllAsync(CancellationToken cancellationToken)
        {
            _store.Clear();
            _pagedStore.Clear();
            return Task.CompletedTask;
        }
    }
}
