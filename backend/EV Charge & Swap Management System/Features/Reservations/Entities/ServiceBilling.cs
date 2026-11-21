namespace EVChargeSwap.Features.Reservations.Entities;

public sealed class Tariff
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid LocationId { get; init; }
    public ReservationServiceType ServiceType { get; init; }
    public decimal UnitPrice { get; init; }
    public DateTimeOffset EffectiveFromUtc { get; init; }
    public Guid CreatedById { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class BatterySwap
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ReservationId { get; init; }
    public Guid ReturnedBatteryId { get; init; }
    public Guid IssuedBatteryId { get; init; }
    public Guid OperatorId { get; init; }
    public required string InspectionResult { get; init; }
    public decimal AppliedRate { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
}

public enum PaymentStatus { Pending = 1, Succeeded, Failed }

public sealed class Payment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid InvoiceId { get; init; }
    public decimal Amount { get; init; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
}

public sealed class PaymentWebhookEvent
{
    public Guid Id { get; init; }
    public Guid PaymentId { get; init; }
    public required string PayloadHash { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; }
}
