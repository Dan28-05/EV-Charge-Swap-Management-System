using Microsoft.AspNetCore.Mvc;
using EVChargeSwap.Common.Pagination;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Locations.Requests;
using EVChargeSwap.Features.Locations.Responses;
using EVChargeSwap.Features.Locations.Services;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;

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

    [HttpGet("nearby")]
    public async Task<ActionResult<PagedResponse<NearbyLocationResponse>>> Nearby(
        [FromQuery] NearbyLocationsRequest request, CancellationToken cancellationToken) =>
        Ok(await service.NearbyAsync(request, cancellationToken));

    [HttpGet("search")]
    public async Task<ActionResult<PagedResponse<LocationResponse>>> Search(
        [FromQuery, Required, StringLength(100, MinimumLength = 1)] string query,
        [FromQuery] PaginationRequest pagination, CancellationToken cancellationToken) =>
        Ok(await service.SearchAsync(query, pagination, cancellationToken));

    [HttpGet("admin")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<PagedResponse<AdminLocationResponse>>> ListAdmin(
        [FromQuery] PaginationRequest pagination, CancellationToken cancellationToken) =>
        Ok(await service.ListAdminAsync(pagination, cancellationToken));

    [HttpPost]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<LocationResponse>> Create(LocationUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<LocationResponse>> Update(Guid id, LocationUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPatch("{id:guid}/visibility")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> SetVisibility(Guid id, LocationVisibilityRequest request,
        CancellationToken cancellationToken) =>
        await service.SetActiveAsync(id, request.IsActive!.Value, cancellationToken) ? NoContent() : NotFound();
}
