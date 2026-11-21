namespace EVChargeSwap.Features.Users.Entities;

public enum NotificationType
{
    Info = 1,
    Success = 2,
    Warning = 3,
    Error = 4
}

public sealed class Notification
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public NotificationType Type { get; init; } = NotificationType.Info;
    public string? ActionUrl { get; init; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAtUtc { get; set; }
}
