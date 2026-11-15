using EVChargeSwap.Data;
using EVChargeSwap.Common.Pagination;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Locations;

public sealed class LocationService(AppDbContext db)
{
    public async Task<PagedResponse<LocationResponse>> ListAsync(PaginationRequest pagination, CancellationToken cancellationToken)
    {
        var query = db.Locations.AsNoTracking().Where(x => x.IsActive);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Skip(pagination.GetOffset())
            .Take(pagination.PageSize)
            .Select(x => new LocationResponse(x.Id, x.Name, x.Address, x.Latitude,
                x.Longitude, x.OffersCarCharging, x.OffersBatterySwap))
            .ToListAsync(cancellationToken);

        return new PagedResponse<LocationResponse>(items, pagination.Page, pagination.PageSize, totalCount);
    }

    public Task<LocationResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Locations.AsNoTracking()
            .Where(x => x.Id == id && x.IsActive)
            .Select(x => new LocationResponse(x.Id, x.Name, x.Address, x.Latitude,
                x.Longitude, x.OffersCarCharging, x.OffersBatterySwap))
            .SingleOrDefaultAsync(cancellationToken);
}
