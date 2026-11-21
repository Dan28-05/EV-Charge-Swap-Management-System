using System.IdentityModel.Tokens.Jwt;
using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Reservations.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations;

[ApiController]
[Authorize(Roles = AppRoles.Operator)]
[Route("api/operator/me/invoices")]
public sealed class OperatorCheckoutController(AppDbContext db, TimeProvider clock, IOutboxService outbox,
    InvoiceRealtime? realtime = null) : ControllerBase
{
    [HttpPost("{id:guid}/cash")]
    public Task<IActionResult> CollectCash(Guid id, CancellationToken ct) => UpdateAsync(id, collectCash: true, ct);

    [HttpPost("{id:guid}/defer")]
    public Task<IActionResult> Defer(Guid id, CancellationToken ct) => UpdateAsync(id, collectCash: false, ct);

    private async Task<IActionResult> UpdateAsync(Guid id, bool collectCash, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var operatorId))
            return Unauthorized();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var station = await (from invoice in db.Invoices
            join reservation in db.Reservations on invoice.ReservationId equals reservation.Id
            where invoice.Id == id
            select (Guid?)reservation.LocationId).SingleOrDefaultAsync(ct);
        if (station is null || !await ReservationLocks.LockOperatorStationAsync(db, station.Value, operatorId, ct))
            return NotFound();

        var invoiceToUpdate = await Checkout.LockInvoiceAsync(db, id, ct);
        if (invoiceToUpdate is null) return NotFound();
        if (collectCash && invoiceToUpdate.Status == InvoiceStatus.Paid &&
            invoiceToUpdate.PaymentReference?.StartsWith("CASH-", StringComparison.Ordinal) == true)
            return NoContent();
        if (invoiceToUpdate.Status != InvoiceStatus.Pending)
            return Problem(statusCode: 409, detail: "Hóa đơn đã đóng hoặc đã được thanh toán.");

        var now = clock.GetUtcNow();
        if (collectCash)
        {
            var pending = await db.Payments.SingleOrDefaultAsync(x => x.InvoiceId == id &&
                x.Status == PaymentStatus.Pending, ct);
            if (pending is not null)
            {
                pending.Status = PaymentStatus.Failed;
                pending.FinishedAtUtc = now;
            }

            var receipt = new Payment
            {
                InvoiceId = id, Amount = invoiceToUpdate.TotalAmount,
                CollectedByOperatorId = operatorId, CreatedAtUtc = now
            };
            db.Payments.Add(receipt);
            await Checkout.SettleAsync(invoiceToUpdate, receipt, now, outbox, ct);
        }
        else
        {
            invoiceToUpdate.DueAtUtc ??= now.AddDays(7);
            invoiceToUpdate.DeferredByOperatorId ??= operatorId;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (realtime is not null) await realtime.TryNotifyAsync(id);
        return NoContent();
    }
}
