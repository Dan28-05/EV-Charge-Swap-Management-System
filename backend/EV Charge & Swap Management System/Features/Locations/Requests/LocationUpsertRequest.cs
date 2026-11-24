using System.ComponentModel.DataAnnotations;
using EVChargeSwap.Features.Locations.Entities;

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
    public LocationOperatingStatus? OperatingStatus { get; init; }
    public bool? IsActive { get; init; }
    public bool IsOpen24Hours { get; init; }
    public int? ChargingPowerKw { get; init; }
    public decimal? BatteryCapacityKwh { get; init; }
    [Range(0, 20)]
    public int? AvailableBatteries { get; init; }
    [Range(0, 10)]
    public int? AvailableChargingBays { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name))
            yield return new ValidationResult("Name is required.", [nameof(Name)]);
        if (string.IsNullOrWhiteSpace(Address))
            yield return new ValidationResult("Address is required.", [nameof(Address)]);
        if (!OffersCarCharging && !OffersBatterySwap)
            yield return new ValidationResult("Select at least one service.",
                [nameof(OffersCarCharging), nameof(OffersBatterySwap)]);
        if (OperatingStatus is { } status && !Enum.IsDefined(status))
            yield return new ValidationResult("Invalid operating status.", [nameof(OperatingStatus)]);
        if (ChargingPowerKw is { } power && (!OffersCarCharging || power is not (30 or 60 or 150 or 250)))
            yield return new ValidationResult("Invalid charging power.", [nameof(ChargingPowerKw)]);
        if (BatteryCapacityKwh is { } capacity && (!OffersBatterySwap || capacity != 1.5m))
            yield return new ValidationResult("Invalid battery capacity.", [nameof(BatteryCapacityKwh)]);
        if (AvailableBatteries is { } batteries && (!OffersBatterySwap || batteries is < 0 or > 20))
            yield return new ValidationResult("Available batteries must be between 0 and 20 and can only be set when offering battery swap.", [nameof(AvailableBatteries)]);
        if (AvailableChargingBays is { } bays && (!OffersCarCharging || bays is < 0 or > 10))
            yield return new ValidationResult("Available charging bays must be between 0 and 10 and can only be set when offering car charging.", [nameof(AvailableChargingBays)]);
    }
}
