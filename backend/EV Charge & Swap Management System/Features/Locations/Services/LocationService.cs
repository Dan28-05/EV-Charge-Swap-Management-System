using EVChargeSwap.Data;
using EVChargeSwap.Common.Pagination;
using Microsoft.EntityFrameworkCore;
using EVChargeSwap.Features.Locations.Caching;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Locations.Requests;
using EVChargeSwap.Features.Locations.Responses;

namespace EVChargeSwap.Features.Locations.Services;

public sealed class LocationService(AppDbContext db, ILocationCache? cache = null)
{
    private readonly ILocationCache _cache = cache ?? new NoOpLocationCache();

    public Task<PagedResponse<LocationResponse>> ListAsync(PaginationRequest pagination, CancellationToken cancellationToken) =>
        ListAsync(pagination, includeInactive: false, cancellationToken);

    public async Task<PagedResponse<AdminLocationResponse>> ListAdminAsync(PaginationRequest pagination,
        CancellationToken cancellationToken)
    {
        var query = db.Locations.AsNoTracking();
        var count = await query.CountAsync(cancellationToken);
        var locations = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip(pagination.GetOffset()).Take(pagination.PageSize).ToListAsync(cancellationToken);
        return new PagedResponse<AdminLocationResponse>(locations
            .Select(x => new AdminLocationResponse(ToResponse(x), x.IsActive)).ToList(),
            pagination.Page, pagination.PageSize, count);
    }

    private async Task<PagedResponse<LocationResponse>> ListAsync(PaginationRequest pagination,
        bool includeInactive, CancellationToken cancellationToken)
    {
        if (!includeInactive)
        {
            var cached = await _cache.GetPagedAsync(pagination.Page, pagination.PageSize, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        var query = db.Locations.AsNoTracking().Where(x => includeInactive || x.IsActive);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Skip(pagination.GetOffset())
            .Take(pagination.PageSize)
            .Select(x => new LocationResponse(x.Id, x.Name, x.Address, x.Latitude,
                x.Longitude, x.OffersCarCharging, x.OffersBatterySwap))
            .ToListAsync(cancellationToken);

        var result = new PagedResponse<LocationResponse>(items, pagination.Page, pagination.PageSize, totalCount);
        if (!includeInactive)
        {
            await _cache.SetPagedAsync(result, cancellationToken);
        }
        return result;
    }

    public async Task<LocationResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var cached = await _cache.GetAsync(id, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var location = await db.Locations.AsNoTracking()
            .Where(x => x.Id == id && x.IsActive)
            .Select(x => new LocationResponse(x.Id, x.Name, x.Address, x.Latitude,
                x.Longitude, x.OffersCarCharging, x.OffersBatterySwap))
            .SingleOrDefaultAsync(cancellationToken);

        if (location is not null)
        {
            await _cache.SetAsync(location, cancellationToken);
        }

        return location;
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
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(x => x.Name.ToLower().Contains(term) || x.Address.ToLower().Contains(term));
        }

        var candidates = await query.Select(x => new LocationResponse(x.Id, x.Name, x.Address,
            x.Latitude, x.Longitude, x.OffersCarCharging, x.OffersBatterySwap)).ToListAsync(cancellationToken);
        var matches = candidates
            .Select(x => new NearbyLocationResponse(x, DistanceKm(latitude, longitude,
                (double)x.Latitude, (double)x.Longitude)))
            .Where(x => x.DistanceKm <= request.RadiusKm)
            .OrderBy(x => x.DistanceKm).ThenBy(x => x.Location.Name).ThenBy(x => x.Location.Id)
            .ToList();
        return new PagedResponse<NearbyLocationResponse>(matches.Skip(request.GetOffset())
            .Take(request.PageSize).ToList(), request.Page, request.PageSize, matches.Count);
    }

    public async Task<LocationResponse> CreateAsync(LocationUpsertRequest request, CancellationToken cancellationToken)
    {
        var location = new Location(request.Name, request.Address, request.Latitude!.Value, request.Longitude!.Value,
            request.OffersCarCharging, request.OffersBatterySwap);
        db.Locations.Add(location);
        await db.SaveChangesAsync(cancellationToken);

        var response = ToResponse(location);
        await _cache.InvalidateAllAsync(cancellationToken);
        await _cache.SetAsync(response, cancellationToken);
        return response;
    }

    public async Task<LocationResponse?> UpdateAsync(Guid id, LocationUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var location = await db.Locations.FindAsync([id], cancellationToken);
        if (location is null) return null;
        location.Update(request.Name, request.Address, request.Latitude!.Value, request.Longitude!.Value,
            request.OffersCarCharging, request.OffersBatterySwap);
        await db.SaveChangesAsync(cancellationToken);

        var response = ToResponse(location);
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
        await db.SaveChangesAsync(cancellationToken);

        await _cache.InvalidateLocationAsync(id, cancellationToken);
        return true;
    }

    private static LocationResponse ToResponse(Location x) =>
        new(x.Id, x.Name, x.Address, x.Latitude, x.Longitude,
            x.OffersCarCharging, x.OffersBatterySwap);

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
