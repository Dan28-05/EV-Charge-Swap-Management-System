using System.ComponentModel.DataAnnotations;

namespace EVChargeSwap.Features.Locations.Requests;

public sealed class NearbyLocationsRequest : IValidatableObject
{
    [Range(-90, 90)]
    public decimal? Latitude { get; init; }

    [Range(-180, 180)]
    public decimal? Longitude { get; init; }

    [Range(1, 100)]
    public int RadiusKm { get; init; } = 10;

    [RegularExpression("^(all|charging|swap)$")]
    public string Service { get; init; } = "all";

    [StringLength(100)]
    public string? Search { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;

    public int GetOffset() => checked((Page - 1) * PageSize);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Latitude is null) yield return new ValidationResult("Latitude is required.", [nameof(Latitude)]);
        if (Longitude is null) yield return new ValidationResult("Longitude is required.", [nameof(Longitude)]);
        if (Page > 0 && PageSize > 0 && (long)(Page - 1) * PageSize > int.MaxValue)
            yield return new ValidationResult("The requested page is too large.", [nameof(Page)]);
    }
}
