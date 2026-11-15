namespace EVChargeSwap.Features.Authentication.Entities;

public sealed class VerificationChallenge
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Channel { get; private set; } = string.Empty;
    public string Destination { get; private set; } = string.Empty;
    public string CodeHash { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }
    public int FailedAttempts { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }

    private VerificationChallenge() { }

    public VerificationChallenge(Guid userId, string channel, string destination, string codeHash, DateTime now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Channel = channel;
        Destination = destination;
        CodeHash = codeHash;
        CreatedAtUtc = now;
        ExpiresAtUtc = now.AddMinutes(5);
        ConcurrencyStamp = Guid.NewGuid();
    }

    public bool IsAvailable(DateTime now) => ConsumedAtUtc is null && ExpiresAtUtc > now && FailedAttempts < 5;
    public void Fail() { FailedAttempts++; ConcurrencyStamp = Guid.NewGuid(); }
    public void Consume(DateTime now) { ConsumedAtUtc = now; ConcurrencyStamp = Guid.NewGuid(); }
}
