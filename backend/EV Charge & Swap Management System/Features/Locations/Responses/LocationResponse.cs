namespace EVChargeSwap.Features.Locations.Responses;

using EVChargeSwap.Features.Locations.Entities;

public sealed record LocationResponse(
    Guid Id,
    string Name,
    string Address,
    decimal Latitude,
    decimal Longitude,
    bool OffersCarCharging,
    bool OffersBatterySwap,
    LocationOperatingStatus OperatingStatus,
    bool IsOpen24Hours = false,
    int? ChargingPowerKw = null,
    decimal? BatteryCapacityKwh = null,
    int? AvailableBatteries = null,
    int? AvailableChargingBays = null);
