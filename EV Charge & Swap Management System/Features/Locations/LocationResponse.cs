namespace EVChargeSwap.Features.Locations;

public sealed record LocationResponse(
    Guid Id,
    string Name,
    string Address,
    decimal Latitude,
    decimal Longitude,
    bool OffersCarCharging,
    bool OffersBatterySwap);
