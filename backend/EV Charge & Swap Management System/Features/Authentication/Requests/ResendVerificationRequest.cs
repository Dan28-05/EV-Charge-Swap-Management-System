using System.ComponentModel.DataAnnotations;

namespace EVChargeSwap.Features.Authentication.Requests;

public sealed class ResendVerificationRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;
    [Required]
    public string Channel { get; init; } = string.Empty;
}
