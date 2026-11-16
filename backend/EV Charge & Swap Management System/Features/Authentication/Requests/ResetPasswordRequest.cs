using System.ComponentModel.DataAnnotations;

namespace EVChargeSwap.Features.Authentication.Requests;

public sealed class ResetPasswordRequest
{
    [Required, StringLength(256)]
    public string Identifier { get; init; } = string.Empty;

    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 8)]
    public string NewPassword { get; init; } = string.Empty;
}
