using System.IdentityModel.Tokens.Jwt;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations;

public sealed record AssignmentResponse(Guid Id, Guid OperatorId, Guid LocationId,
    Guid? AssignedByAdminId, DateTimeOffset? AssignedAtUtc, DateTimeOffset? EndedAtUtc);

[ApiController]
[Route("api/operators")]
public sealed class OperatorAssignmentsController(AppDbContext db, OperatorAssignmentService service) : ControllerBase
{
    [HttpGet("me/location")]
    [Authorize(Roles = AppRoles.Operator)]
    public async Task<IActionResult> MyLocation(CancellationToken ct)
    {
        if (!TryGetUserId(out var operatorId)) return Unauthorized();
        var location = await (from assignment in db.Set<OperatorAssignment>()
            join station in db.Locations on assignment.LocationId equals station.Id
            where assignment.OperatorId == operatorId && assignment.EndedAtUtc == null
            select new { station.Id, station.Name, station.Address }).SingleOrDefaultAsync(ct);
        return location is null ? NotFound() : Ok(location);
    }

    [HttpGet("{operatorId:guid}/assignments")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> History(Guid operatorId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == operatorId, ct)) return NotFound();
        var items = await db.Set<OperatorAssignment>().AsNoTracking()
            .Where(x => x.OperatorId == operatorId)
            .OrderByDescending(x => x.AssignedAtUtc)
            .Select(x => new AssignmentResponse(x.Id, x.OperatorId, x.LocationId,
                x.AssignedByAdminId, x.AssignedAtUtc, x.EndedAtUtc)).ToListAsync(ct);
        return Ok(items);
    }

    [HttpPut("{operatorId:guid}/assignment")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Transfer(Guid operatorId, AssignLocationRequest request, CancellationToken ct) =>
        await Assign(operatorId, request.LocationId, true, ct);

    [HttpDelete("{operatorId:guid}/assignment")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> End(Guid operatorId, CancellationToken ct) =>
        await service.EndAsync(operatorId, ct) == 1 ? NoContent() : NotFound();

    internal async Task<IActionResult> Assign(Guid operatorId, Guid locationId, bool transfer, CancellationToken ct)
    {
        if (!TryGetUserId(out var adminId)) return Unauthorized();
        var result = await service.AssignAsync(operatorId, locationId, adminId, transfer, ct);
        if (result.Assignment is { } a)
            return StatusCode(transfer ? 200 : 201, new AssignmentResponse(a.Id, a.OperatorId,
                a.LocationId, a.AssignedByAdminId, a.AssignedAtUtc, a.EndedAtUtc));
        return Problem(statusCode: result.Error == "The location does not exist." ? 404 : 409, detail: result.Error);
    }

    private bool TryGetUserId(out Guid id) => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);
}

public sealed record AssignLocationRequest(Guid LocationId);
