namespace EVChargeSwap.Features.Users.Responses;

public sealed record UserResponse(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    bool EmailConfirmed,
    bool PhoneNumberConfirmed,
    bool IsActive,
    IReadOnlyCollection<string> Roles);
