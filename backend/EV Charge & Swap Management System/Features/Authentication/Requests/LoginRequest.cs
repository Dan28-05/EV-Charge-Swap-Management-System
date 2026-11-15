using System.ComponentModel.DataAnnotations;

namespace EVChargeSwap.Features.Authentication.Requests;

public sealed class LoginRequest
{
    [Required, StringLength(256)]
    public string Identifier { get; init; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; init; } = string.Empty;
}
