using System.ComponentModel.DataAnnotations;

namespace EVChargeSwap.Features.Authentication.Requests;

public sealed class VerifyContactRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;
    [Required]
    public string Channel { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; init; } = string.Empty;
}
