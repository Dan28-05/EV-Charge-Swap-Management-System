using System.Net;
using System.Net.Http.Json;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Locations.Responses;
using EVChargeSwap.Features.Reservations.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class OperatorResourcesApiTests
{
    [Fact]
    public async Task Public_availability_uses_real_resources_and_operator_actions_require_authentication()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(new ChargingBay { LocationId = factory.ActiveLocationId, Code = "A01", PowerKw = 60 });
            db.Add(new Battery { LocationId = factory.ActiveLocationId, Serial = "PIN-TEST", State = BatteryState.Ready });
            await db.SaveChangesAsync();
        }

        var location = await client.GetFromJsonAsync<LocationResponse>($"/api/locations/{factory.ActiveLocationId}");
        Assert.NotNull(location);
        Assert.Equal(1, location.AvailableChargingBays);
        Assert.Equal(1, location.AvailableBatteries);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/operator/me/station")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/api/operator/me/service-hours",
            new { serviceType = 1, day = 1, intervals = Array.Empty<object>() })).StatusCode);
    }
}
