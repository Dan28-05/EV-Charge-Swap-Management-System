namespace EVChargeSwap.Features.Locations.Caching;

using EVChargeSwap.Common.Pagination;
using EVChargeSwap.Features.Locations.Responses;

public interface ILocationCache
{
    Task<long> GetVersionAsync(CancellationToken cancellationToken) => Task.FromResult(0L);
    Task<LocationResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task SetAsync(LocationResponse location, CancellationToken cancellationToken);
    Task SetAsync(LocationResponse location, long expectedVersion, CancellationToken cancellationToken) =>
        SetAsync(location, cancellationToken);
    Task<PagedResponse<LocationResponse>?> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task SetPagedAsync(PagedResponse<LocationResponse> pagedResponse, CancellationToken cancellationToken);
    Task SetPagedAsync(PagedResponse<LocationResponse> pagedResponse, long expectedVersion, CancellationToken cancellationToken) =>
        SetPagedAsync(pagedResponse, cancellationToken);
    Task InvalidateLocationAsync(Guid id, CancellationToken cancellationToken);
    Task InvalidateAllAsync(CancellationToken cancellationToken);
}
