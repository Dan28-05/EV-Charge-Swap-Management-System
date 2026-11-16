using System.ComponentModel.DataAnnotations;

namespace EVChargeSwap.Features.Authentication.Requests;

public sealed class ForgotPasswordRequest
{
    [Required, StringLength(256)]
    public string Identifier { get; init; } = string.Empty;
}
