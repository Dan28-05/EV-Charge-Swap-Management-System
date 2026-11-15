namespace EVChargeSwap.Features.Locations.Responses;

public sealed record LocationResponse(
    Guid Id,
    string Name,
    string Address,
    decimal Latitude,
    decimal Longitude,
    bool OffersCarCharging,
    bool OffersBatterySwap);
