using System.ComponentModel.DataAnnotations;

namespace EVChargeSwap.Features.Authentication.Requests;

public sealed class RefreshTokenRequest
{
    [Required, StringLength(512)]
    public string RefreshToken { get; init; } = string.Empty;
}
