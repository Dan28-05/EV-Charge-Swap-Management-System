using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class ReservationsApiTests
{
    [Fact]
    public async Task Anonymous_caller_cannot_create_holds_or_check_in()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var locationId = factory.ActiveLocationId;

        var hold = await client.PostAsJsonAsync("/api/reservations", new { locationId, serviceType = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, hold.StatusCode);

        var checkIn = await client.PostAsJsonAsync("/api/reservations/check-in",
            new { locationId, code = new string('A', 32) });
        Assert.Equal(HttpStatusCode.Unauthorized, checkIn.StatusCode);

        var arrive = await client.PostAsync($"/api/operator/me/charging-reservations/{Guid.NewGuid()}/vehicle-arrived", null);
        Assert.Equal(HttpStatusCode.Unauthorized, arrive.StatusCode);
        var chargingStatus = await client.GetAsync($"/api/operator/me/charging-reservations/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, chargingStatus.StatusCode);

        var setup = await client.PostAsJsonAsync($"/api/locations/{locationId}/charging-bays",
            new { code = "A01", powerKw = 60 });
        Assert.Equal(HttpStatusCode.Unauthorized, setup.StatusCode);
    }
}
