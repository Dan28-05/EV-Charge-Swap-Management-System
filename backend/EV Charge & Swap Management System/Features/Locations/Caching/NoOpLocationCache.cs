namespace EVChargeSwap.Features.Locations.Caching;

using EVChargeSwap.Common.Pagination;
using EVChargeSwap.Features.Locations.Responses;

public sealed class NoOpLocationCache : ILocationCache
{
    public Task<LocationResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<LocationResponse?>(null);

    public Task SetAsync(LocationResponse location, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<PagedResponse<LocationResponse>?> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult<PagedResponse<LocationResponse>?>(null);

    public Task SetPagedAsync(PagedResponse<LocationResponse> pagedResponse, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task InvalidateLocationAsync(Guid id, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task InvalidateAllAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
