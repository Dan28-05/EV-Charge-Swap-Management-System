using Microsoft.AspNetCore.Mvc;
using EVChargeSwap.Common.Pagination;

namespace EVChargeSwap.Features.Locations;

[ApiController]
[Route("api/locations")]
public sealed class LocationsController(LocationService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<LocationResponse>>> List(
        [FromQuery] PaginationRequest pagination, CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(pagination, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LocationResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var location = await service.GetAsync(id, cancellationToken);
        return location is null ? NotFound() : Ok(location);
    }
}
