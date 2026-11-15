using System.ComponentModel.DataAnnotations;

namespace EVChargeSwap.Features.Locations.Requests;

public sealed class LocationUpsertRequest : IValidatableObject
{
    [Required, StringLength(160)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(300)]
    public string Address { get; init; } = string.Empty;

    [Required, Range(-90, 90)]
    public decimal? Latitude { get; init; }

    [Required, Range(-180, 180)]
    public decimal? Longitude { get; init; }

    public bool OffersCarCharging { get; init; }
    public bool OffersBatterySwap { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name))
            yield return new ValidationResult("Name is required.", [nameof(Name)]);
        if (string.IsNullOrWhiteSpace(Address))
            yield return new ValidationResult("Address is required.", [nameof(Address)]);
        if (!OffersCarCharging && !OffersBatterySwap)
            yield return new ValidationResult("Select at least one service.",
                [nameof(OffersCarCharging), nameof(OffersBatterySwap)]);
    }
}
