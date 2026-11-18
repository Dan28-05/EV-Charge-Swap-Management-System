using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EVChargeSwap.Features.Reservations;

public sealed record CreateBayRequest([Required, StringLength(64)] string Code, [Range(1, 1000)] int PowerKw);
public sealed record CreateBatteryRequest([Required, StringLength(64)] string Serial);
public sealed record CreateHoursRequest(
    [EnumDataType(typeof(ReservationServiceType))] ReservationServiceType ServiceType,
    [EnumDataType(typeof(DayOfWeek))] DayOfWeek Day,
    [Range(0, 1439)] int OpensAtMinute,
    [Range(1, 1440)] int ClosesAtMinute);
public sealed record AssignOperatorRequest(Guid OperatorId);

[ApiController]
[Authorize(Roles = AppRoles.Admin)]
[Route("api/locations/{locationId:guid}")]
public sealed class ReservationSetupController(AppDbContext db, OperatorAssignmentService assignments) : ControllerBase
{
    [HttpPost("charging-bays")]
    public Task<IActionResult> CreateBay(Guid locationId, CreateBayRequest request, CancellationToken ct) =>
        AddResourceAsync(locationId, ReservationServiceType.Charging, new ChargingBay
        {
            LocationId = locationId, Code = request.Code.Trim().ToUpperInvariant(), PowerKw = request.PowerKw
        }, ct);

    [HttpPost("batteries")]
    public Task<IActionResult> CreateBattery(Guid locationId, CreateBatteryRequest request, CancellationToken ct) =>
        AddResourceAsync(locationId, ReservationServiceType.BatterySwap, new Battery
        {
            LocationId = locationId, Serial = request.Serial.Trim().ToUpperInvariant(), State = BatteryState.Ready
        }, ct);

    [HttpPost("service-hours")]
    public Task<IActionResult> CreateHours(Guid locationId, CreateHoursRequest request, CancellationToken ct)
    {
        if (request.OpensAtMinute >= request.ClosesAtMinute)
            return Task.FromResult<IActionResult>(Problem(statusCode: 400, detail: "Opening must precede closing."));

        return AddResourceAsync(locationId, request.ServiceType, new ServiceHours
        {
            LocationId = locationId,
            ServiceType = request.ServiceType,
            Day = request.Day,
            OpensAtMinute = request.OpensAtMinute,
            ClosesAtMinute = request.ClosesAtMinute
        }, ct);
    }

    [HttpPost("operators")]
    public async Task<IActionResult> AssignOperator(Guid locationId, AssignOperatorRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var adminId)) return Unauthorized();
        var result = await assignments.AssignAsync(request.OperatorId, locationId, adminId, false, ct);
        if (result.Assignment is { } assignment)
            return StatusCode(201, new AssignmentResponse(assignment.Id, assignment.OperatorId,
                assignment.LocationId, assignment.AssignedByAdminId, assignment.AssignedAtUtc, assignment.EndedAtUtc));
        return Problem(statusCode: result.Error == "The location does not exist." ? 404 : 409, detail: result.Error);
    }

    private async Task<IActionResult> AddResourceAsync(Guid locationId, ReservationServiceType serviceType,
        object resource, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == locationId, ct);
        if (location is null) return NotFound();

        var offered = serviceType == ReservationServiceType.Charging
            ? location.OffersCarCharging
            : location.OffersBatterySwap;
        return offered ? await InsertAsync(resource, ct)
            : Problem(statusCode: 409, detail: "The location does not offer this service.");
    }

    private async Task<IActionResult> InsertAsync(object entity, CancellationToken ct)
    {
        db.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
            return StatusCode(StatusCodes.Status201Created, entity);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(statusCode: 409, detail: "This entry already exists.");
        }
    }
}
