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

}
