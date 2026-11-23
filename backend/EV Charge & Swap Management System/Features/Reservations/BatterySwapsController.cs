using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Locations.Services;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EVChargeSwap.Features.Reservations;

public sealed record CompleteBatterySwapRequest(
    [Required, StringLength(64)] string ReturnedSerial,
    [Required, StringLength(64)] string IssuedSerial,
    [Required, StringLength(500)] string InspectionResult);

public sealed record BatterySwapCompletionResponse(Guid Id, Guid ReservationId, Guid ReturnedBatteryId,
    Guid IssuedBatteryId, Guid OperatorId, string InspectionResult, decimal AppliedRate,
    DateTimeOffset CompletedAtUtc, Invoice Invoice)
{
    public static BatterySwapCompletionResponse From(BatterySwap swap, Invoice invoice) =>
        new(swap.Id, swap.ReservationId, swap.ReturnedBatteryId, swap.IssuedBatteryId, swap.OperatorId,
            swap.InspectionResult, swap.AppliedRate, swap.CompletedAtUtc, invoice);
}

[ApiController]
[Authorize(Roles = AppRoles.Operator)]
[Route("api/operator/me/battery-swaps")]
public sealed class BatterySwapsController(AppDbContext db, TimeProvider clock, IOutboxService outbox,
    IStationNotifier notifier, InvoiceRealtime? realtime = null) : ControllerBase
{
    [HttpPost("{reservationId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid reservationId, CompleteBatterySwapRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var operatorId))
            return Unauthorized();
        var returnedSerial = request.ReturnedSerial.Trim().ToUpperInvariant();
        var issuedSerial = request.IssuedSerial.Trim().ToUpperInvariant();
        if (returnedSerial == issuedSerial || returnedSerial.Length is 0 or > 64 || issuedSerial.Length is 0 or > 64 ||
            string.IsNullOrWhiteSpace(request.InspectionResult))
            return Problem(statusCode: 400, detail: "Two distinct battery serials and an inspection result are required.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var station = await db.Reservations.Where(x => x.Id == reservationId && x.BatteryId != null)
            .Select(x => (Guid?)x.LocationId).SingleOrDefaultAsync(ct);
        if (station is not { } id || !await ReservationLocks.LockOperatorStationAsync(db, id, operatorId, ct))
            return NotFound();

        var reservation = await db.Reservations.AsNoTracking().SingleAsync(x => x.Id == reservationId, ct);
        if (reservation.Status == ReservationStatus.Completed)
        {
            var inspection = request.InspectionResult.Trim();
            var previous = await (from priorSwap in db.BatterySwaps
                join returnedBattery in db.Batteries on priorSwap.ReturnedBatteryId equals returnedBattery.Id
                join issuedBattery in db.Batteries on priorSwap.IssuedBatteryId equals issuedBattery.Id
                join savedInvoice in db.Invoices on priorSwap.ReservationId equals savedInvoice.ReservationId
                where priorSwap.ReservationId == reservationId && returnedBattery.Serial.ToUpper() == returnedSerial &&
                    issuedBattery.Serial.ToUpper() == issuedSerial && priorSwap.InspectionResult == inspection
                select new { Swap = priorSwap, Invoice = savedInvoice }).SingleOrDefaultAsync(ct);
            return previous is null
                ? Problem(statusCode: 409, detail: "The completed swap does not match this request.")
                : Ok(BatterySwapCompletionResponse.From(previous.Swap, previous.Invoice));
        }

        if (reservation.Status != ReservationStatus.CheckedIn)
            return Problem(statusCode: 409, detail: "The battery reservation must be checked in.");
        var issued = await db.Batteries.SingleOrDefaultAsync(x => x.Id == reservation.BatteryId &&
            x.LocationId == id && x.Serial.ToUpper() == issuedSerial && x.State == BatteryState.Ready, ct);
        if (issued is null) return Problem(statusCode: 409, detail: "The reserved battery must be ready at this station.");

        var returned = await db.Batteries.FromSqlInterpolated(
            $"""SELECT * FROM batteries WHERE UPPER("Serial") = {returnedSerial} FOR UPDATE""").SingleOrDefaultAsync(ct);
        if (returned is not null && (returned.State != BatteryState.WithCustomer ||
            returned.CustodianCustomerId != reservation.CustomerId))
            return Problem(statusCode: 409, detail: "The returned battery is not in this customer's custody.");

        var now = clock.GetUtcNow();
        var rate = await TariffLookup.FindAsync(db, id, ReservationServiceType.BatterySwap, now, ct);
        if (rate is null) return Problem(statusCode: 409, detail: "Configure a battery-swap tariff before confirming handover.");
        if (returned is null)
        {
            returned = new Battery { Serial = returnedSerial };
            db.Batteries.Add(returned);
        }
        UpdateCustody(returned, BatteryState.Inspection, id, null, operatorId, now);
        UpdateCustody(issued, BatteryState.WithCustomer, null, reservation.CustomerId, operatorId, now);

        // Release the hold before moving the issued battery out of station inventory.
        await db.Reservations.Where(x => x.Id == reservationId).ExecuteUpdateAsync(update => update
            .SetProperty(x => x.Status, ReservationStatus.Completed)
            .SetProperty(x => x.ClosedById, operatorId)
            .SetProperty(x => x.ClosedAtUtc, now), ct);
        var swap = new BatterySwap { ReservationId = reservationId, ReturnedBatteryId = returned.Id,
            IssuedBatteryId = issued.Id, OperatorId = operatorId, InspectionResult = request.InspectionResult.Trim(),
            AppliedRate = rate.Value, CompletedAtUtc = now };
        db.BatterySwaps.Add(swap);
        var invoice = new Invoice
        {
            ReservationId = reservationId, CustomerId = reservation.CustomerId, KwhRate = 0m,
            SwapRate = rate.Value, TotalAmount = rate.Value, CreatedAtUtc = now
        };
        await Checkout.AddInvoiceAsync(db, outbox, invoice, ct);
        await ReservationTransitions.RecordAsync(db, outbox, reservation, ReservationStatus.Completed, operatorId, now, ct);
        await outbox.EnqueueAsync("BatterySwapCompleted", EventRoutingKeys.BatterySwapCompleted,
            new BatterySwapCompletedEvent(reservationId, reservation.CustomerId, rate.Value), ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_batteries_Serial" })
        {
            return Problem(statusCode: 409, detail: "The returned serial was registered concurrently. Refresh and verify custody.");
        }

        await notifier.NotifyStationInventoryChangedAsync(id, ct);
        if (realtime is not null) await realtime.TryNotifyAsync(invoice.Id);
        return Ok(BatterySwapCompletionResponse.From(swap, invoice));
    }

    private void UpdateCustody(Battery battery, BatteryState state, Guid? location, Guid? customer,
        Guid operatorId, DateTimeOffset now)
    {
        db.Entry(battery).Property(x => x.LocationId).CurrentValue = location;
        db.Entry(battery).Property(x => x.State).CurrentValue = state;
        battery.CustodianCustomerId = customer;
        battery.StateUpdatedById = operatorId;
        battery.StateUpdatedAtUtc = now;
    }
}
