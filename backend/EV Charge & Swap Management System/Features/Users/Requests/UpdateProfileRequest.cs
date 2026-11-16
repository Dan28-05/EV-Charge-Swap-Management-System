using System.ComponentModel.DataAnnotations;

namespace EVChargeSwap.Features.Users.Requests;

public sealed class UpdateProfileRequest
{
    [Required, StringLength(160, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;
}
