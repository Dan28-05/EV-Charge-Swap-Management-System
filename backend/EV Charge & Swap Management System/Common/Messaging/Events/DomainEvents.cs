using System;

namespace EVChargeSwap.Common.Messaging.Events;

public static class EventRoutingKeys
{
    public const string ReservationConfirmed = "reservation.confirmed";
    public const string ReservationCancelled = "reservation.cancelled";
    public const string ReservationExpired = "reservation.expired";
    public const string CustomerCheckedIn = "reservation.checked-in";
    public const string ReservationCompleted = "reservation.completed";
    public const string ChargingSessionCompleted = "charging.session.completed";
    public const string BatterySwapCompleted = "battery.swap.completed";
    public const string PaymentSucceeded = "payment.succeeded";
    public const string InvoiceGenerated = "billing.invoice.generated";
    public const string BayFaulted = "equipment.bay.faulted";
    public const string IncidentReported = "incident.reported";
    public const string IncidentReservationResolved = "incident.reservation.resolved";
}

public sealed record ReservationConfirmedEvent(
    Guid ReservationId,
    Guid CustomerId,
    Guid LocationId,
    string Code,
    string ServiceType,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record ChargingSessionCompletedEvent(
    Guid SessionId,
    Guid ReservationId,
    Guid CustomerId,
    Guid ChargingBayId,
    decimal StartMeterKwh,
    decimal EndMeterKwh,
    decimal TotalKwh,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    decimal? AppliedKwhRate = null);

public sealed record BatterySwapCompletedEvent(Guid ReservationId, Guid CustomerId, decimal AppliedRate);
public sealed record PaymentSucceededEvent(Guid CustomerId, Guid InvoiceId);

public sealed record ReservationTransitionEvent(
    Guid ReservationId,
    Guid CustomerId,
    Guid LocationId,
    string Status,
    Guid? ActorId,
    DateTimeOffset OccurredAtUtc,
    string? Reason);

public sealed record InvoiceGeneratedEvent(
    Guid InvoiceId,
    Guid ReservationId,
    Guid CustomerId,
    decimal TotalKwh,
    decimal EnergyAmount,
    decimal TotalAmount,
    DateTimeOffset CreatedAtUtc,
    bool IsBatterySwap = false);

public sealed record ChargingBayFaultedEvent(
    Guid ChargingBayId,
    Guid LocationId,
    string BayCode,
    string Reason,
    DateTimeOffset FaultedAtUtc);

public sealed record IncidentReportedEvent(Guid IncidentId);

public sealed record IncidentReservationResolvedEvent(Guid CustomerId, string? ReplacementCode);

