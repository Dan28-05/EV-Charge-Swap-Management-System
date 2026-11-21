using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Locations.Services;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations;

public sealed record HoldRequest(Guid LocationId, [EnumDataType(typeof(ReservationServiceType))] ReservationServiceType ServiceType);
public sealed record CheckInRequest(Guid LocationId, [Required, StringLength(32, MinimumLength = 32)] string Code);
public sealed record ReservationResponse(Guid Id, Guid LocationId, Guid? ChargingBayId, Guid? BatteryId,
    string Status, string? Code, DateTimeOffset ExpiresAtUtc, DateTimeOffset? CheckedInAtUtc, string? ClosingReason,
    DateTimeOffset? VehicleArrivedAtUtc, DateTimeOffset? VehicleDepartedAtUtc, DateTimeOffset? NoChargeConfirmedAtUtc,
    string? ResourceCode = null, DateTimeOffset? ChargingStartedAtUtc = null, DateTimeOffset? ChargingCompletedAtUtc = null,
    decimal? InvoiceTotalAmount = null, string? InvoiceStatus = null);

[ApiController]
[Route("api/reservations")]
public sealed class ReservationsController(AppDbContext db, ReservationService service, TimeProvider clock,
    IStationNotifier stationNotifier) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = AppRoles.Customer)]
    public async Task<ActionResult<ReservationResponse>> Hold(HoldRequest request,
        [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (idempotencyKey == Guid.Empty || Request.Headers["Idempotency-Key"].Count > 1 ||
            (idempotencyKey is null && Request.Headers.ContainsKey("Idempotency-Key")))
            return Problem(statusCode: 400, detail: "Idempotency-Key must be a single non-empty UUID.");

        var result = await service.HoldAsync(userId, request.LocationId, request.ServiceType, ct, idempotencyKey);
        if (result.Reservation is { } reservation)
        {
            // The reservation is already committed. Signal local clients before the Outbox replay.
            if (!result.Replayed)
                await stationNotifier.NotifyStationInventoryChangedAsync(reservation.LocationId, ct);
            var response = await CustomerResponseAsync(reservation, ct);
            return result.Replayed
                ? Ok(response)
                : CreatedAtAction(nameof(Get), new { id = reservation.Id }, response);
        }

        return result.Conflict is null ? NotFound() : Problem(statusCode: 409, detail: result.Conflict);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = AppRoles.Customer)]
    public async Task<ActionResult<ReservationResponse>> Get(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var reservation = await db.Set<Reservation>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.CustomerId == userId, ct);
        return reservation is null ? NotFound() : Ok(await CustomerResponseAsync(reservation, ct));
    }

    [HttpGet("active")]
    [Authorize(Roles = AppRoles.Customer)]
    public async Task<ActionResult<ReservationResponse>> GetActive(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var now = clock.GetUtcNow();
        var reservation = await service.Active.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CustomerId == userId &&
                (x.Status == ReservationStatus.CheckedIn || x.ExpiresAtUtc > now), ct);
        return reservation is null ? NotFound() : Ok(await CustomerResponseAsync(reservation, ct));
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = AppRoles.Customer)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (await service.CancelAsync(userId, id, ct) == 1) return NoContent();
        var status = await db.Set<Reservation>().Where(x => x.Id == id && x.CustomerId == userId)
            .Select(x => (ReservationStatus?)x.Status).SingleOrDefaultAsync(ct);
        return status switch
        {
            null => NotFound(),
            ReservationStatus.Cancelled => NoContent(),
            _ => Problem(statusCode: 409, detail: "Only an unexpired hold can be cancelled.")
        };
    }

    [HttpPost("check-in")]
    [Authorize(Roles = AppRoles.Operator)]
    public async Task<IActionResult> CheckIn(CheckInRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!await db.Set<OperatorAssignment>().AnyAsync(x => x.OperatorId == userId &&
                x.LocationId == request.LocationId && x.EndedAtUtc == null, ct))
            return Forbid();

        var code = request.Code.Trim().ToUpperInvariant();
        if (await service.CheckInAsync(request.LocationId, userId, code, ct) != 1)
            return Problem(statusCode: 409, detail: "The code is invalid, expired, or already used.");

        var reservation = await db.Set<Reservation>().AsNoTracking().SingleAsync(x => x.Code == code, ct);
        return Ok(ToResponse(reservation));
    }

    [HttpPost("{id:guid}/close-without-service")]
    [Authorize(Roles = AppRoles.Operator)]
    public async Task<IActionResult> CloseWithoutService(Guid id, [FromQuery] Guid locationId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!await db.Set<OperatorAssignment>().AnyAsync(x => x.OperatorId == userId &&
                x.LocationId == locationId && x.EndedAtUtc == null, ct))
            return Forbid();

        return await service.CloseWithoutServiceAsync(locationId, userId, id, ct) == 1
            ? NoContent()
            : Problem(statusCode: 409, detail: "The reservation is not checked in or the bay is still occupied.");
    }

    private async Task<ReservationResponse> CustomerResponseAsync(Reservation reservation, CancellationToken ct)
    {
        var response = ToResponse(reservation);
        if (reservation.Status is ReservationStatus.CheckedIn or ReservationStatus.Completed)
        {
            var invoice = await db.Invoices.AsNoTracking().Where(x => x.ReservationId == reservation.Id)
                .Select(x => new { x.TotalAmount, x.Status }).SingleOrDefaultAsync(ct);
            if (invoice is not null)
                response = response with { InvoiceTotalAmount = invoice.TotalAmount, InvoiceStatus = invoice.Status.ToString() };
        }
        if (reservation.ChargingBayId is { } bayId)
        {
            var code = await db.ChargingBays.Where(x => x.Id == bayId).Select(x => x.Code).SingleAsync(ct);
            var session = await db.ChargingSessions.Where(x => x.ReservationId == reservation.Id)
                .Select(x => new { x.StartedAtUtc, x.CompletedAtUtc }).SingleOrDefaultAsync(ct);
            return response with { ResourceCode = code, ChargingStartedAtUtc = session?.StartedAtUtc,
                ChargingCompletedAtUtc = session?.CompletedAtUtc };
        }

        return response with { ResourceCode = await db.Batteries.Where(x => x.Id == reservation.BatteryId)
            .Select(x => x.Serial).SingleAsync(ct) };
    }

    private ReservationResponse ToResponse(Reservation reservation)
    {
        var status = reservation.Status == ReservationStatus.Held && reservation.ExpiresAtUtc <= clock.GetUtcNow()
            ? ReservationStatus.Expired
            : reservation.Status;
        return new ReservationResponse(reservation.Id, reservation.LocationId, reservation.ChargingBayId,
            reservation.BatteryId, status.ToString(), status == ReservationStatus.Held ? reservation.Code : null,
            reservation.ExpiresAtUtc, reservation.CheckedInAtUtc, reservation.ClosingReason,
            reservation.VehicleArrivedAtUtc, reservation.VehicleDepartedAtUtc, reservation.NoChargeConfirmedAtUtc);
    }

    private bool TryGetUserId(out Guid id) => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);
}
