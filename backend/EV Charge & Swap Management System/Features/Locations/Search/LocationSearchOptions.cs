namespace EVChargeSwap.Features.Locations.Search;

public sealed class LocationSearchOptions
{
    public const string SectionName = "LocationSearch";

    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public string Alias { get; init; } = "charging-stations";
    public bool Enabled => !string.IsNullOrWhiteSpace(Endpoint);
}
