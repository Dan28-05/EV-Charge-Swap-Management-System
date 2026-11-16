namespace EVChargeSwap.Features.Locations.Entities;

public sealed class Location
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public decimal Latitude { get; private set; }
    public decimal Longitude { get; private set; }
    public bool OffersCarCharging { get; private set; }
    public bool OffersBatterySwap { get; private set; }
    public bool IsActive { get; private set; }
    public LocationOperatingStatus OperatingStatus { get; private set; } = LocationOperatingStatus.Unknown;
    public bool IsOpen24Hours { get; private set; }
    public int? ChargingPowerKw { get; private set; }
    public decimal? BatteryCapacityKwh { get; private set; }
    public int? AvailableBatteries { get; private set; }
    public int? AvailableChargingBays { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private Location() { } // EF Core

    public Location(string name, string address, decimal latitude, decimal longitude,
        bool offersCarCharging, bool offersBatterySwap)
    {
        SetDetails(name, address, latitude, longitude, offersCarCharging, offersBatterySwap);
        Id = Guid.NewGuid();
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public void Update(string name, string address, decimal latitude, decimal longitude,
        bool offersCarCharging, bool offersBatterySwap) =>
        SetDetails(name, address, latitude, longitude, offersCarCharging, offersBatterySwap);

    private void SetDetails(string name, string address, decimal latitude, decimal longitude,
        bool offersCarCharging, bool offersBatterySwap)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Address is required.", nameof(address));
        if (name.Trim().Length > 160) throw new ArgumentException("Name cannot exceed 160 characters.", nameof(name));
        if (address.Trim().Length > 300) throw new ArgumentException("Address cannot exceed 300 characters.", nameof(address));
        if (latitude is < -90 or > 90) throw new ArgumentOutOfRangeException(nameof(latitude));
        if (longitude is < -180 or > 180) throw new ArgumentOutOfRangeException(nameof(longitude));
        if (!offersCarCharging && !offersBatterySwap)
            throw new ArgumentException("A location must offer at least one service.");

        Name = name.Trim();
        Address = address.Trim();
        Latitude = latitude;
        Longitude = longitude;
        OffersCarCharging = offersCarCharging;
        OffersBatterySwap = offersBatterySwap;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    public void SetOperatingStatus(LocationOperatingStatus status)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        OperatingStatus = status;
    }

    public void SetServiceDetails(bool isOpen24Hours, int? chargingPowerKw, decimal? batteryCapacityKwh, int? availableBatteries = null, int? availableChargingBays = null)
    {
        if (chargingPowerKw is not null && (!OffersCarCharging || chargingPowerKw is not (30 or 60 or 150 or 250)))
            throw new ArgumentException("Invalid charging power for this location.", nameof(chargingPowerKw));
        if (batteryCapacityKwh is not null && (!OffersBatterySwap || batteryCapacityKwh != 1.5m))
            throw new ArgumentException("Invalid battery capacity for this location.", nameof(batteryCapacityKwh));
        if (availableBatteries is not null && (!OffersBatterySwap || availableBatteries is < 0 or > 20))
            throw new ArgumentException("Invalid available batteries for this location.", nameof(availableBatteries));
        if (availableChargingBays is not null && (!OffersCarCharging || availableChargingBays is < 0 or > 10))
            throw new ArgumentException("Invalid available charging bays for this location.", nameof(availableChargingBays));
        IsOpen24Hours = isOpen24Hours;
        ChargingPowerKw = chargingPowerKw;
        BatteryCapacityKwh = batteryCapacityKwh;
        AvailableBatteries = availableBatteries;
        AvailableChargingBays = availableChargingBays;
    }

}
