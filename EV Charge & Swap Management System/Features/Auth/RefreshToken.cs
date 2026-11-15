namespace EVChargeSwap.Features.Auth;

public sealed class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public ApplicationUser User { get; private set; } = null!;
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? RevokedReason { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }

    private RefreshToken() { } // EF Core

    public RefreshToken(Guid userId, Guid familyId, string tokenHash,
        DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User ID is required.", nameof(userId));
        if (familyId == Guid.Empty) throw new ArgumentException("Family ID is required.", nameof(familyId));
        if (string.IsNullOrWhiteSpace(tokenHash)) throw new ArgumentException("Token hash is required.", nameof(tokenHash));
        if (createdAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Token timestamps must be UTC.");
        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Expiration must be after creation time.", nameof(expiresAtUtc));

        Id = Guid.NewGuid();
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public bool IsActive(DateTime utcNow) => RevokedAtUtc is null && ExpiresAtUtc > utcNow;

    public void Revoke(DateTime revokedAtUtc, string reason, string? replacementTokenHash = null)
    {
        if (RevokedAtUtc is not null) return;
        if (revokedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Revocation time must be UTC.", nameof(revokedAtUtc));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Revocation reason is required.", nameof(reason));

        RevokedAtUtc = revokedAtUtc;
        RevokedReason = reason;
        ReplacedByTokenHash = replacementTokenHash;
        ConcurrencyStamp = Guid.NewGuid();
    }
}
