using Microsoft.AspNetCore.Identity;

namespace EVChargeSwap.Features.Users.Entities;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private ApplicationUser() { } // EF Core

    public ApplicationUser(string email, string fullName, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name is required.", nameof(fullName));

        email = email.Trim();
        fullName = fullName.Trim();
        if (fullName.Length > 160)
            throw new ArgumentException("Full name cannot exceed 160 characters.", nameof(fullName));
        if (createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Creation time must be UTC.", nameof(createdAtUtc));

        Id = Guid.NewGuid();
        Email = email;
        UserName = email;
        FullName = fullName;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    public void UpdateFullName(string fullName)
    {
        fullName = fullName.Trim();
        if (fullName.Length is < 2 or > 160)
            throw new ArgumentException("Full name must contain 2 to 160 characters.", nameof(fullName));
        FullName = fullName;
    }
}
