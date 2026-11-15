using System.ComponentModel.DataAnnotations;

namespace EVChargeSwap.Features.Users.Requests;

public sealed class CreateOperatorRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(160, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;

    [Phone, StringLength(30)]
    public string? PhoneNumber { get; init; }

    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;
}
