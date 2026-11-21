namespace EVChargeSwap.Data.Entities;

public sealed class AuditLog
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Action { get; init; } = string.Empty;
    public Guid? ActorId { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public string? Details { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}
