namespace EVChargeSwap.Features.Reservations.Entities;

public enum InvoiceStatus
{
    Pending = 1,
    Paid = 2,
    Cancelled = 3
}

public sealed class Invoice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ReservationId { get; init; }
    public Guid CustomerId { get; init; }
    public decimal TotalKwh { get; init; }
    public required decimal KwhRate { get; init; }
    public decimal EnergyAmount { get; init; }
    public decimal? SwapRate { get; init; }
    public decimal TotalAmount { get; init; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Pending;
    public string? PaymentReference { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAtUtc { get; set; }
}
