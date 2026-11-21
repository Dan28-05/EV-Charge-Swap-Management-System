using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Locations.Services;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations;

public sealed record ResolveIncidentRequest(
    [EnumDataType(typeof(IncidentResolution))] IncidentResolution Resolution, Guid? ReplacementId);
public sealed record IncidentSummary(Guid Id, Guid? ChargingBayId, Guid? BatteryId, Guid? ReservationId,
    Guid? ChargingSessionId, string Description, DateTimeOffset ReportedAtUtc);

[ApiController]
[Authorize(Roles = AppRoles.Operator)]
[Route("api/operator/me/incidents")]
public sealed class IncidentsController(IncidentService incidents, IStationNotifier notifier) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IncidentSummary>>> List(
        [FromQuery, Range(1, int.MaxValue / 50)] int page = 1, CancellationToken ct = default) =>
        Ok(await incidents.ForOperator(UserId()).Where(x => x.Resolution == null)
            .OrderBy(x => x.ReportedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * 50).Take(50)
            .Select(x => new IncidentSummary(x.Id, x.ChargingBayId, x.BatteryId,
                x.ReservationId, x.ChargingSessionId, x.Description, x.ReportedAtUtc)).ToListAsync(ct));

    [HttpGet("{id:guid}/options")]
    public async Task<ActionResult<IncidentOptions>> Options(Guid id, CancellationToken ct)
    {
        var options = await incidents.GetOptionsAsync(id, UserId(), ct);
        return options is null ? NotFound() : Ok(options);
    }

    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, ResolveIncidentRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Resolution) || request.ReplacementId == Guid.Empty ||
            (request.Resolution == IncidentResolution.Reassigned) != request.ReplacementId.HasValue)
            return Problem(statusCode: 400, detail: "Only reassignment requires a replacement resource ID.");

        var result = await incidents.ResolveAsync(id, UserId(), request.Resolution, request.ReplacementId, ct);
        if (result.LocationId is not { } locationId)
            return result.Conflict is null ? NotFound() : Problem(statusCode: 409, detail: result.Conflict);

        if (request.Resolution != IncidentResolution.Recorded)
            await notifier.NotifyStationInventoryChangedAsync(locationId, ct);
        return NoContent();
    }

    private Guid UserId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
