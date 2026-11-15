using System.ComponentModel.DataAnnotations;

namespace EVChargeSwap.Features.Locations.Requests;

public sealed class LocationVisibilityRequest
{
    [Required]
    public bool? IsActive { get; init; }
}
