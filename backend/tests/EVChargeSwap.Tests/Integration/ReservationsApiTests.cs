using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EVChargeSwap.Features.Authentication.Services;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class ReservationsApiTests
{
    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_idempotency_key_returns_bad_request(string key)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser("reservation-key@example.test", "Reservation customer", DateTime.UtcNow);
        Assert.True((await users.CreateAsync(user)).Succeeded);
        var jwt = scope.ServiceProvider.GetRequiredService<JwtTokenService>()
            .CreateAccessToken(user, [AppRoles.Customer]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Value);
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);

        var response = await client.PostAsJsonAsync("/api/reservations",
            new { locationId = factory.ActiveLocationId, serviceType = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

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
