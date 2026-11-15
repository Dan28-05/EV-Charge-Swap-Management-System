using EVChargeSwap.Features.Users.Responses;

namespace EVChargeSwap.Features.Auth.Responses;

public sealed record AuthResponse(
    string TokenType,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    UserResponse User);
