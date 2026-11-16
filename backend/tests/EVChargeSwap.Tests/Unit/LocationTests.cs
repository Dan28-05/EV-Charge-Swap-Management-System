using EVChargeSwap.Features.Locations.Entities;
using Xunit;

namespace EVChargeSwap.Tests.Unit;

public sealed class LocationTests
{
    [Fact]
    public void Creation_trims_text_and_sets_a_usable_identity()
    {
        var location = new Location("  Station A  ", "  Address A  ", 10, 106, true, false);
        Assert.Equal("Station A", location.Name);
        Assert.Equal("Address A", location.Address);
        Assert.NotEqual(Guid.Empty, location.Id);
        Assert.True(location.IsActive);
        Assert.Equal(LocationOperatingStatus.Unknown, location.OperatingStatus);
        Assert.Equal(DateTimeKind.Utc, location.CreatedAtUtc.Kind);
    }

    [Theory]
    [InlineData("", "Address")]
    [InlineData("   ", "Address")]
    [InlineData("Station", "")]
    [InlineData("Station", "   ")]
    public void Required_text_cannot_be_blank(string name, string address) =>
        Assert.Throws<ArgumentException>(() => new Location(name, address, 10, 106, true, false));

    [Fact]
    public void A_location_must_offer_a_service() =>
        Assert.Throws<ArgumentException>(() => new Location("Station", "Address", 10, 106, false, false));

    [Theory]
    [InlineData(-91, 0)]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    [InlineData(0, 181)]
    public void Invalid_coordinates_are_rejected(int latitude, int longitude) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Location("Station", "Address", latitude, longitude, true, false));

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    public void Boundary_coordinates_are_valid(int latitude, int longitude)
    {
        var location = new Location("Station", "Address", latitude, longitude, false, true);
        Assert.Equal(latitude, location.Latitude);
        Assert.Equal(longitude, location.Longitude);
    }

    [Fact]
    public void Text_cannot_exceed_database_limits()
    {
        Assert.Throws<ArgumentException>(() => new Location(new string('x', 161), "Address", 0, 0, true, false));
        Assert.Throws<ArgumentException>(() => new Location("Station", new string('x', 301), 0, 0, true, false));
    }

    [Fact]
    public void A_location_can_be_deactivated()
    {
        var location = new Location("Station", "Address", 0, 0, true, false);
        location.Deactivate();
        Assert.False(location.IsActive);
    }

    [Fact]
    public void Operating_status_is_manually_set_and_visibility_is_independent()
    {
        var location = new Location("Station", "Address", 0, 0, true, false);
        location.SetOperatingStatus(LocationOperatingStatus.Maintenance);
        location.Deactivate();
        Assert.Equal(LocationOperatingStatus.Maintenance, location.OperatingStatus);
        location.Activate();
        location.SetOperatingStatus(LocationOperatingStatus.Closed);
        Assert.Equal(LocationOperatingStatus.Closed, location.OperatingStatus);
        Assert.Throws<ArgumentOutOfRangeException>(() => location.SetOperatingStatus((LocationOperatingStatus)99));
    }

    [Fact]
    public void Service_details_follow_offered_services_and_supported_values()
    {
        var location = new Location("Station", "Address", 0, 0, true, true);
        location.SetServiceDetails(true, 250, 1.5m, 15, 8);
        Assert.True(location.IsOpen24Hours);
        Assert.Equal(250, location.ChargingPowerKw);
        Assert.Equal(1.5m, location.BatteryCapacityKwh);
        Assert.Equal(15, location.AvailableBatteries);
        Assert.Equal(8, location.AvailableChargingBays);
        Assert.Throws<ArgumentException>(() => location.SetServiceDetails(true, 100, 1.5m, 10, 5));
        Assert.Throws<ArgumentException>(() => location.SetServiceDetails(true, 30, 2m, 10, 5));
        Assert.Throws<ArgumentException>(() => location.SetServiceDetails(true, 30, 1.5m, 21, 5));
        Assert.Throws<ArgumentException>(() => location.SetServiceDetails(true, 30, 1.5m, -1, 5));
        Assert.Throws<ArgumentException>(() => location.SetServiceDetails(true, 30, 1.5m, 10, 11));
        Assert.Throws<ArgumentException>(() => location.SetServiceDetails(true, 30, 1.5m, 10, -1));

        var chargingOnly = new Location("Charging Station", "Address", 0, 0, true, false);
        Assert.Throws<ArgumentException>(() => chargingOnly.SetServiceDetails(false, 60, null, 10, 5));

        var swapOnly = new Location("Swap Station", "Address", 0, 0, false, true);
        Assert.Throws<ArgumentException>(() => swapOnly.SetServiceDetails(false, null, 1.5m, 10, 5));
    }
}
