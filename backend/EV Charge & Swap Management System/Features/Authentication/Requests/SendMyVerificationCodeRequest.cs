using System.ComponentModel.DataAnnotations;

namespace EVChargeSwap.Features.Authentication.Requests;

public sealed class SendMyVerificationCodeRequest
{
    [Required]
    public string Channel { get; init; } = string.Empty;
}
