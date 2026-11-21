namespace EVChargeSwap.Data.Entities;

public sealed class InboxMessage
{
    public Guid Id { get; init; }
    public string ConsumerName { get; init; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAtUtc { get; set; }
}
