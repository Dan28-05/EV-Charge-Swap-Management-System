using EVChargeSwap.Data;
using EVChargeSwap.Common.Pagination;
using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Common.Messaging.Events;
using Microsoft.EntityFrameworkCore;
using EVChargeSwap.Features.Locations.Caching;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Locations.Requests;
using EVChargeSwap.Features.Locations.Responses;
using EVChargeSwap.Features.Locations.Search;
using EVChargeSwap.Features.Reservations.Entities;
using Microsoft.Extensions.Options;

namespace EVChargeSwap.Features.Locations.Services;

public sealed class LocationService(AppDbContext db, ILocationCache? cache = null,
    LocationSearchService? searchService = null, IOutboxService? outbox = null,
    IOptions<LocationSearchOptions>? searchOptions = null, TimeProvider? clock = null)
{
    private readonly ILocationCache _cache = cache ?? new NoOpLocationCache();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<PagedResponse<LocationResponse>> ListAsync(PaginationRequest pagination, CancellationToken cancellationToken) =>
        ListAsync(pagination, includeInactive: false, cancellationToken);

    public async Task<PagedResponse<LocationResponse>> SearchAsync(string search, PaginationRequest pagination,
        CancellationToken cancellationToken)
    {
        var offset = pagination.GetOffset();
        if (searchService is not null &&
            await searchService.TrySearchAsync(search, offset, pagination.PageSize, cancellationToken) is { } indexed)
        {
            var current = await ReadSearchResultsAsync(indexed.Ids, cancellationToken);
            // Visibility can change before indexing. Re-run the page against authoritative data in that window.
            if (current.Count == indexed.Ids.Count)
                return new(current, pagination.Page, pagination.PageSize, indexed.TotalCount);
        }

        var fallback = await LocationSearchService.SearchPostgresAsync(db, search, offset, pagination.PageSize, cancellationToken);
        return new(await ReadSearchResultsAsync(fallback.Ids, cancellationToken),
            pagination.Page, pagination.PageSize, fallback.TotalCount);
    }

    private async Task<List<LocationResponse>> ReadSearchResultsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var locations = await db.Locations.AsNoTracking()
            .Where(x => x.IsActive && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var ordered = ids.Where(locations.ContainsKey).Select(id => ToResponse(locations[id])).ToList();
        return await WithLiveCountsAsync(ordered, ct);
    }

    public async Task<PagedResponse<AdminLocationResponse>> ListAdminAsync(PaginationRequest pagination,
        CancellationToken cancellationToken)
    {
        var query = db.Locations.AsNoTracking();
        var count = await query.CountAsync(cancellationToken);
        var locations = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip(pagination.GetOffset()).Take(pagination.PageSize).ToListAsync(cancellationToken);
        var responses = await WithLiveCountsAsync(locations.Select(ToResponse).ToList(), cancellationToken);
        return new PagedResponse<AdminLocationResponse>(responses
            .Select((x, i) => new AdminLocationResponse(x, locations[i].IsActive)).ToList(),
            pagination.Page, pagination.PageSize, count);
    }

    private async Task<PagedResponse<LocationResponse>> ListAsync(PaginationRequest pagination,
        bool includeInactive, CancellationToken cancellationToken)
    {
        long cacheVersion = 0;
        if (!includeInactive)
        {
            cacheVersion = await _cache.GetVersionAsync(cancellationToken);
            var cached = await _cache.GetPagedAsync(pagination.Page, pagination.PageSize, cancellationToken);
            if (cached is not null)
                return cached with { Items = await WithLiveCountsAsync(cached.Items.ToList(), cancellationToken) };
        }
        var query = db.Locations.AsNoTracking().Where(x => includeInactive || x.IsActive);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.OperatingStatus == LocationOperatingStatus.Open)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Skip(pagination.GetOffset())
            .Take(pagination.PageSize)
            .Select(x => new LocationResponse(x.Id, x.Name, x.Address, x.Latitude,
                x.Longitude, x.OffersCarCharging, x.OffersBatterySwap, x.OperatingStatus,
                x.IsOpen24Hours, x.ChargingPowerKw, x.BatteryCapacityKwh, x.AvailableBatteries, x.AvailableChargingBays))
            .ToListAsync(cancellationToken);

        var result = new PagedResponse<LocationResponse>(await WithLiveCountsAsync(items, cancellationToken),
            pagination.Page, pagination.PageSize, totalCount);
        if (!includeInactive) await _cache.SetPagedAsync(result, cacheVersion, cancellationToken);
        return result;
    }

    public async Task<LocationResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var cacheVersion = await _cache.GetVersionAsync(cancellationToken);
        var cached = await _cache.GetAsync(id, cancellationToken);
        if (cached is not null)
            return (await WithLiveCountsAsync([cached], cancellationToken))[0];
        var location = await db.Locations.AsNoTracking()
            .Where(x => x.Id == id && x.IsActive)
            .Select(x => new LocationResponse(x.Id, x.Name, x.Address, x.Latitude,
                x.Longitude, x.OffersCarCharging, x.OffersBatterySwap, x.OperatingStatus,
                x.IsOpen24Hours, x.ChargingPowerKw, x.BatteryCapacityKwh, x.AvailableBatteries, x.AvailableChargingBays))
            .SingleOrDefaultAsync(cancellationToken);

        if (location is null) return null;
        await _cache.SetAsync(location, cacheVersion, cancellationToken);
        return (await WithLiveCountsAsync([location], cancellationToken))[0];
    }

    public async Task<PagedResponse<NearbyLocationResponse>> NearbyAsync(NearbyLocationsRequest request,
        CancellationToken cancellationToken)
    {
        var latitude = (double)request.Latitude!.Value;
        var longitude = (double)request.Longitude!.Value;
        // Slightly generous bounds; the exact Haversine check below decides inclusion.
        var latitudeDelta = request.RadiusKm / 110.0;
        var longitudeDelta = request.RadiusKm /
            (110.0 * Math.Max(0.01, Math.Abs(Math.Cos(latitude * Math.PI / 180))));
        var query = db.Locations.AsNoTracking().Where(x => x.IsActive &&
            x.Latitude >= (decimal)Math.Max(-90, latitude - latitudeDelta) &&
            x.Latitude <= (decimal)Math.Min(90, latitude + latitudeDelta));
        if (longitude - longitudeDelta >= -180 && longitude + longitudeDelta <= 180)
            query = query.Where(x => x.Longitude >= (decimal)(longitude - longitudeDelta) &&
                x.Longitude <= (decimal)(longitude + longitudeDelta));
        if (request.Service == "charging") query = query.Where(x => x.OffersCarCharging);
        if (request.Service == "swap") query = query.Where(x => x.OffersBatterySwap);
        if (request.Availability is "open" or "available")
            query = query.Where(x => x.OperatingStatus == LocationOperatingStatus.Open);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(x => x.Name.ToLower().Contains(term) || x.Address.ToLower().Contains(term));
        }

        var candidates = await query.Select(x => new LocationResponse(x.Id, x.Name, x.Address,
            x.Latitude, x.Longitude, x.OffersCarCharging, x.OffersBatterySwap, x.OperatingStatus,
            x.IsOpen24Hours, x.ChargingPowerKw, x.BatteryCapacityKwh, x.AvailableBatteries, x.AvailableChargingBays)).ToListAsync(cancellationToken);
        candidates = await WithLiveCountsAsync(candidates, cancellationToken);
        var matches = candidates
            .Select(x => new NearbyLocationResponse(x, DistanceKm(latitude, longitude,
                (double)x.Latitude, (double)x.Longitude)))
            .Where(x => x.DistanceKm <= request.RadiusKm &&
                (request.Availability != "available" || IsOpenAndAvailable(x.Location, request.Service)))
            .OrderByDescending(x => IsOpenAndAvailable(x.Location, request.Service))
            .ThenByDescending(x => x.Location.OperatingStatus == LocationOperatingStatus.Open)
            .ThenBy(x => x.DistanceKm).ThenBy(x => x.Location.Name).ThenBy(x => x.Location.Id)
            .ToList();
        return new PagedResponse<NearbyLocationResponse>(matches.Skip(request.GetOffset())
            .Take(request.PageSize).ToList(), request.Page, request.PageSize, matches.Count);
    }

    public async Task<LocationResponse> CreateAsync(LocationUpsertRequest request, CancellationToken cancellationToken)
    {
        var location = new Location(request.Name, request.Address, request.Latitude!.Value, request.Longitude!.Value,
            request.OffersCarCharging, request.OffersBatterySwap);
        if (request.OperatingStatus is { } status) location.SetOperatingStatus(status);
        if (request.IsActive == false) location.Deactivate();
        location.SetServiceDetails(request.IsOpen24Hours, request.ChargingPowerKw, request.BatteryCapacityKwh, request.AvailableBatteries, request.AvailableChargingBays);
        db.Locations.Add(location);
        await SaveLocationAsync(location.Id, cancellationToken);

        var response = (await WithLiveCountsAsync([ToResponse(location)], cancellationToken))[0];
        await _cache.InvalidateAllAsync(cancellationToken);
        if (location.IsActive)
        {
            await _cache.SetAsync(response, cancellationToken);
        }
        return response;
    }

    public async Task<LocationResponse?> UpdateAsync(Guid id, LocationUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var location = await db.Locations.FindAsync([id], cancellationToken);
        if (location is null) return null;
        location.Update(request.Name, request.Address, request.Latitude!.Value, request.Longitude!.Value,
            request.OffersCarCharging, request.OffersBatterySwap);
        if (request.OperatingStatus is { } status) location.SetOperatingStatus(status);
        location.SetServiceDetails(request.IsOpen24Hours, request.ChargingPowerKw, request.BatteryCapacityKwh, request.AvailableBatteries, request.AvailableChargingBays);
        await SaveLocationAsync(id, cancellationToken);

        var response = (await WithLiveCountsAsync([ToResponse(location)], cancellationToken))[0];
        await _cache.InvalidateLocationAsync(id, cancellationToken);
        if (location.IsActive)
        {
            await _cache.SetAsync(response, cancellationToken);
        }
        return response;
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var location = await db.Locations.FindAsync([id], cancellationToken);
        if (location is null) return false;
        if (isActive) location.Activate(); else location.Deactivate();
        await SaveLocationAsync(id, cancellationToken);

        await _cache.InvalidateLocationAsync(id, cancellationToken);
        return true;
    }

    private async Task SaveLocationAsync(Guid locationId, CancellationToken ct)
    {
        if (searchOptions?.Value.Enabled == true && outbox is not null)
            await outbox.EnqueueAsync("LocationUpdated", EventRoutingKeys.LocationUpdated,
                new LocationUpdatedEvent(locationId), ct);

        // Both tracked changes are persisted by the same EF Core transaction.
        await db.SaveChangesAsync(ct);
    }

    private static LocationResponse ToResponse(Location x) =>
        new(x.Id, x.Name, x.Address, x.Latitude, x.Longitude,
            x.OffersCarCharging, x.OffersBatterySwap, x.OperatingStatus,
            x.IsOpen24Hours, x.ChargingPowerKw, x.BatteryCapacityKwh, x.AvailableBatteries, x.AvailableChargingBays);

    public async Task<IReadOnlyList<StationInventoryUpdatedEvent>> GetInventorySnapshotAsync(
        Guid[] locationIds, CancellationToken ct)
    {
        if (locationIds.Length == 0) return [];
        var stations = await db.Locations.AsNoTracking()
            .Where(x => x.IsActive && locationIds.Contains(x.Id))
            .Select(x => new LocationResponse(x.Id, string.Empty, string.Empty, 0, 0,
                x.OffersCarCharging, x.OffersBatterySwap, LocationOperatingStatus.Unknown,
                false, null, null, null, null))
            .ToListAsync(ct);
        var inventory = await WithLiveCountsAsync(stations, ct);
        var now = _clock.GetUtcNow();
        return inventory.Select(x => new StationInventoryUpdatedEvent(
            x.Id, x.AvailableChargingBays, x.AvailableBatteries, now)).ToList();
    }

    private async Task<List<LocationResponse>> WithLiveCountsAsync(List<LocationResponse> locations, CancellationToken ct)
    {
        if (locations.Count == 0) return locations;
        var ids = locations.Select(x => x.Id).ToArray();
        var now = _clock.GetUtcNow();
        var activeReservations = await db.Set<Reservation>().AsNoTracking()
            .Where(r => ids.Contains(r.LocationId) &&
                (r.Status == ReservationStatus.CheckedIn || r.Status == ReservationStatus.Held))
            .Select(r => new { r.Status, r.ExpiresAtUtc, r.ChargingBayId, r.BatteryId }).ToListAsync(ct);
        // SQLite in HTTP tests cannot translate DateTimeOffset comparisons; expiry is applied after the bounded read.
        var heldResources = activeReservations.Where(r => r.Status == ReservationStatus.CheckedIn || r.ExpiresAtUtc > now);
        var heldBayIds = heldResources.Where(x => x.ChargingBayId.HasValue)
            .Select(x => x.ChargingBayId!.Value).ToHashSet();
        var heldBatteryIds = heldResources.Where(x => x.BatteryId.HasValue)
            .Select(x => x.BatteryId!.Value).ToHashSet();
        var chargingIds = locations.Where(x => x.OffersCarCharging).Select(x => x.Id).ToArray();
        var swapIds = locations.Where(x => x.OffersBatterySwap).Select(x => x.Id).ToArray();

        var bayCounts = chargingIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await db.Set<ChargingBay>().AsNoTracking()
                .Where(x => chargingIds.Contains(x.LocationId) &&
                    x.EquipmentState == ChargerState.Active &&
                    x.ParkingState == ParkingState.Vacant &&
                    !heldBayIds.Contains(x.Id))
                .GroupBy(x => x.LocationId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var batteryCounts = swapIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await db.Set<Battery>().AsNoTracking()
                .Where(x => x.LocationId != null &&
                    swapIds.Contains(x.LocationId.Value) &&
                    x.State == BatteryState.Ready &&
                    !heldBatteryIds.Contains(x.Id))
                .GroupBy(x => x.LocationId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return locations.Select(x => x with
        {
            AvailableChargingBays = x.OffersCarCharging ? bayCounts.GetValueOrDefault(x.Id) : null,
            AvailableBatteries = x.OffersBatterySwap ? batteryCounts.GetValueOrDefault(x.Id) : null
        }).ToList();
    }

    private static bool IsOpenAndAvailable(LocationResponse location, string service) =>
        location.OperatingStatus == LocationOperatingStatus.Open && service switch
        {
            "charging" => location.OffersCarCharging && location.AvailableChargingBays > 0,
            "swap" => location.OffersBatterySwap && location.AvailableBatteries > 0,
            _ => (location.OffersCarCharging && location.AvailableChargingBays > 0) ||
                 (location.OffersBatterySwap && location.AvailableBatteries > 0)
        };

    private static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371.0088;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Pow(Math.Sin(dLat / 2), 2) +
            Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
            Math.Pow(Math.Sin(dLon / 2), 2);
        return earthRadiusKm * 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
