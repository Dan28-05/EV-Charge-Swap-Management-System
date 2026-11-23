using System.IdentityModel.Tokens.Jwt;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations;

public sealed record MeterReadingRequest(decimal MeterKwh);
public sealed record ChargingSessionResponse(Guid Id, Guid ReservationId, Guid ChargingBayId,
    decimal StartMeterKwh, decimal? EndMeterKwh, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    Guid StartedById, Guid? CompletedById);
public sealed record ChargingReservationStatusResponse(Guid ReservationId, Guid ChargingBayId, string Status,
    DateTimeOffset? VehicleArrivedAtUtc, DateTimeOffset? NoChargeConfirmedAtUtc,
    DateTimeOffset? VehicleDepartedAtUtc, ChargingSessionResponse? Session);

[ApiController]
[Authorize(Roles = AppRoles.Operator)]
[Route("api/operator/me/charging-reservations")]
public sealed class ChargingController(AppDbContext db, ChargingService service, InvoiceRealtime? realtime = null) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChargingReservationStatusResponse>> Get(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var operatorId)) return Unauthorized();
        var reservation = await db.Set<Reservation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id &&
            x.ChargingBayId != null && db.Set<OperatorAssignment>().Any(a => a.LocationId == x.LocationId &&
                a.OperatorId == operatorId && a.EndedAtUtc == null), ct);
        if (reservation is null) return NotFound();
        var session = await db.Set<ChargingSession>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.ReservationId == id, ct);
        return Ok(new ChargingReservationStatusResponse(reservation.Id, reservation.ChargingBayId!.Value,
            reservation.Status.ToString(), reservation.VehicleArrivedAtUtc, reservation.NoChargeConfirmedAtUtc,
            reservation.VehicleDepartedAtUtc, session is null ? null : ToResponse(session)));
    }

    [HttpPost("{id:guid}/vehicle-arrived")]
    public async Task<IActionResult> VehicleArrived(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var operatorId)) return Unauthorized();
        return await service.VehicleArrivedAsync(id, operatorId, ct) ? NoContent() :
            Problem(statusCode: 409, detail: "Lượt đặt hoặc trạng thái chỗ đỗ đã thay đổi. Kiểm tra lại thông tin trên dashboard.");
    }

    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id, MeterReadingRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var operatorId)) return Unauthorized();
        var session = await service.StartAsync(id, operatorId, request.MeterKwh, ct);
        return session is null
            ? Problem(statusCode: 409, detail: "Charging cannot start for this reservation or meter reading.")
            : Ok(ToResponse(session));
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, MeterReadingRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var operatorId)) return Unauthorized();
        if (!await service.CompleteAsync(id, operatorId, request.MeterKwh, ct))
            return Problem(statusCode: 409, detail: "Charging cannot be completed for this reservation or meter reading.");

        var invoice = await db.Invoices.AsNoTracking().SingleAsync(x => x.ReservationId == id, ct);
        if (realtime is not null) await realtime.TryNotifyAsync(invoice.Id);
        return Ok(invoice);
    }

    [HttpPost("{id:guid}/no-charge")]
    public async Task<IActionResult> NoCharge(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var operatorId)) return Unauthorized();
        return await service.ConfirmNoChargeAsync(id, operatorId, ct)
            ? NoContent()
            : Problem(statusCode: 409, detail: "This parked vehicle cannot be marked as not charged.");
    }

    [HttpPost("{id:guid}/vehicle-departed")]
    public async Task<IActionResult> VehicleDeparted(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var operatorId)) return Unauthorized();
        return await service.VehicleDepartedAsync(id, operatorId, ct)
            ? NoContent()
            : Problem(statusCode: 409, detail: "Finish charging or confirm no charge before the vehicle departs.");
    }

    private bool TryGetUserId(out Guid operatorId) =>
        Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out operatorId);

    internal static ChargingSessionResponse ToResponse(ChargingSession session) => new(session.Id,
        session.ReservationId, session.ChargingBayId, session.StartMeterKwh, session.EndMeterKwh,
        session.StartedAtUtc, session.CompletedAtUtc, session.StartedById, session.CompletedById);
}

[ApiController]
[Authorize(Roles = AppRoles.Customer)]
[Route("api/reservations")]
public sealed class CustomerChargingSessionController(AppDbContext db) : ControllerBase
{
    [HttpGet("{id:guid}/charging-session")]
    public async Task<ActionResult<ChargingSessionResponse>> Get(Guid id, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var customerId)) return Unauthorized();
        var session = await db.Set<ChargingSession>().AsNoTracking()
            .Where(x => x.ReservationId == id && db.Set<Reservation>().Any(r => r.Id == id && r.CustomerId == customerId))
            .SingleOrDefaultAsync(ct);
        return session is null ? NotFound() : Ok(ChargingController.ToResponse(session));
    }
}
