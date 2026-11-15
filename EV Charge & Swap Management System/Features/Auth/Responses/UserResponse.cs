namespace EVChargeSwap.Features.Auth.Responses;

public sealed record UserResponse(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    bool IsActive,
    IReadOnlyCollection<string> Roles);
