using EVChargeSwap.Data;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Users.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations.Services;

public sealed class InvoiceRealtime(AppDbContext db, IHubContext<NotificationHub> hub,
    ILogger<InvoiceRealtime> logger)
{
    public async Task NotifyAsync(Guid invoiceId, CancellationToken ct)
    {
        var audience = await (from invoice in db.Invoices.AsNoTracking()
            join reservation in db.Reservations.AsNoTracking() on invoice.ReservationId equals reservation.Id
            where invoice.Id == invoiceId
            select new { invoice.CustomerId, invoice.ReservationId, reservation.LocationId }).SingleOrDefaultAsync(ct);
        if (audience is null) return;

        var operators = await db.Set<OperatorAssignment>().AsNoTracking()
            .Where(x => x.LocationId == audience.LocationId && x.EndedAtUtc == null)
            .Select(x => x.OperatorId).ToListAsync(ct);
        var groups = operators.Append(audience.CustomerId).Distinct().Select(NotificationHub.GroupFor).ToList();
        await hub.Clients.Groups(groups).SendAsync("InvoiceChanged", invoiceId, ct);
        await hub.Clients.Group(NotificationHub.GroupFor(audience.CustomerId))
            .SendAsync("ReservationChanged", audience.ReservationId, ct);
    }

    public async Task TryNotifyAsync(Guid invoiceId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await NotifyAsync(invoiceId, timeout.Token); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not deliver realtime refresh for invoice {InvoiceId}.", invoiceId);
        }
    }
}
