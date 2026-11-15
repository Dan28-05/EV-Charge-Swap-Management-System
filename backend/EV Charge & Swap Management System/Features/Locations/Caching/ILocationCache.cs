namespace EVChargeSwap.Features.Locations.Caching;

using EVChargeSwap.Common.Pagination;
using EVChargeSwap.Features.Locations.Responses;

public interface ILocationCache
{
    Task<LocationResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task SetAsync(LocationResponse location, CancellationToken cancellationToken);
    Task<PagedResponse<LocationResponse>?> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task SetPagedAsync(PagedResponse<LocationResponse> pagedResponse, CancellationToken cancellationToken);
    Task InvalidateLocationAsync(Guid id, CancellationToken cancellationToken);
    Task InvalidateAllAsync(CancellationToken cancellationToken);
}
