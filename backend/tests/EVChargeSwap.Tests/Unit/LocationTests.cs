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
}
