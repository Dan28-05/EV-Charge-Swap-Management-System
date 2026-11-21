using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using EVChargeSwap.Data.Entities;
using EVChargeSwap.Features.Reservations.Entities;

namespace EVChargeSwap.Features.Reservations.Services;

internal static class ReservationTransitions
{
    // The caller saves the audit row and outbox message in the transaction that changes the reservation.
    public static async Task RecordAsync(AppDbContext db, IOutboxService? outbox, Reservation reservation,
        ReservationStatus status, Guid? actorId, DateTimeOffset occurredAtUtc, CancellationToken ct,
        string? reason = null)
    {
        var (eventType, routingKey) = status switch
        {
            ReservationStatus.Held => ("ReservationConfirmed", EventRoutingKeys.ReservationConfirmed),
            ReservationStatus.Cancelled => ("ReservationCancelled", EventRoutingKeys.ReservationCancelled),
            ReservationStatus.Expired => ("ReservationExpired", EventRoutingKeys.ReservationExpired),
            ReservationStatus.CheckedIn => ("CustomerCheckedIn", EventRoutingKeys.CustomerCheckedIn),
            ReservationStatus.Completed => ("ReservationCompleted", EventRoutingKeys.ReservationCompleted),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        db.AuditLogs.Add(new AuditLog
        {
            Action = eventType,
            ActorId = actorId,
            ResourceType = nameof(Reservation),
            ResourceId = reservation.Id.ToString(),
            Details = reason,
            TimestampUtc = occurredAtUtc
        });
        if (outbox is null) return;

        if (status == ReservationStatus.Held)
        {
            var service = reservation.ChargingBayId.HasValue
                ? ReservationServiceType.Charging : ReservationServiceType.BatterySwap;
            await outbox.EnqueueAsync(eventType, routingKey, new ReservationConfirmedEvent(
                reservation.Id, reservation.CustomerId, reservation.LocationId, reservation.Code,
                service.ToString(), reservation.ExpiresAtUtc, occurredAtUtc), ct);
            return;
        }

        await outbox.EnqueueAsync(eventType, routingKey, new ReservationTransitionEvent(
            reservation.Id, reservation.CustomerId, reservation.LocationId, status.ToString(),
            actorId, occurredAtUtc, reason), ct);
    }
}
