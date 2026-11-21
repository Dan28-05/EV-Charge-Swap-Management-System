using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Reservations.Entities;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations.Services;

internal static class Checkout
{
    public static Task<Invoice?> LockInvoiceAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        db.Invoices.FromSqlInterpolated($"""SELECT * FROM invoices WHERE "Id" = {id} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);

    public static Invoice Charging(Reservation reservation, decimal kwh, decimal rate, DateTimeOffset now)
    {
        var amount = Math.Round(kwh * rate, 2);
        return new Invoice
        {
            ReservationId = reservation.Id, CustomerId = reservation.CustomerId, CreatedAtUtc = now,
            TotalKwh = kwh, KwhRate = rate, EnergyAmount = amount, TotalAmount = amount
        };
    }

    public static async Task AddInvoiceAsync(AppDbContext db, IOutboxService? outbox, Invoice invoice,
        CancellationToken ct)
    {
        db.Invoices.Add(invoice);
        if (outbox is not null)
            await outbox.EnqueueAsync("InvoiceGenerated", EventRoutingKeys.InvoiceGenerated,
                new InvoiceGeneratedEvent(invoice.Id, invoice.ReservationId, invoice.CustomerId,
                    invoice.TotalKwh, invoice.EnergyAmount, invoice.TotalAmount, invoice.CreatedAtUtc,
                    invoice.SwapRate.HasValue), ct);
    }

    public static async Task SettleAsync(Invoice invoice, Payment payment, DateTimeOffset now,
        IOutboxService outbox, CancellationToken ct)
    {
        payment.Status = PaymentStatus.Succeeded;
        payment.FinishedAtUtc = now;
        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAtUtc = now;
        invoice.PaymentReference = payment.CollectedByOperatorId.HasValue
            ? $"CASH-{payment.Id:N}" : $"MOCK-{payment.Id:N}";
        await outbox.EnqueueAsync("PaymentSucceeded", EventRoutingKeys.PaymentSucceeded,
            new PaymentSucceededEvent(invoice.CustomerId, invoice.Id), ct);
    }
}
