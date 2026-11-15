using EVChargeSwap.Features.Users.Responses;

namespace EVChargeSwap.Features.Authentication.Responses;

public sealed record AuthenticationResponse(
    string TokenType,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    UserResponse User);
