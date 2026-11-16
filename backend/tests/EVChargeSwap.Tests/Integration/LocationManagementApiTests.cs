using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EVChargeSwap.Common.Pagination;
using EVChargeSwap.Features.Authentication.Requests;
using EVChargeSwap.Features.Authentication.Responses;
using EVChargeSwap.Features.Locations.Requests;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Locations.Responses;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class LocationManagementApiTests
{
    [Fact]
    public async Task Search_finds_stations_across_locations_and_excludes_hidden()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var result = await client.GetFromJsonAsync<PagedResponse<LocationResponse>>(
            "/api/locations/search?query=B%20Station&page=1&pageSize=20");
        Assert.NotNull(result);
        Assert.Contains(result.Items, x => x.Name == "B Station");
        var hidden = await client.GetFromJsonAsync<PagedResponse<LocationResponse>>(
            "/api/locations/search?query=hidden&page=1&pageSize=20");
        Assert.NotNull(hidden);
        Assert.Empty(hidden.Items);
    }
    [Fact]
    public async Task Nearby_filters_by_radius_service_and_excludes_hidden_locations()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetFromJsonAsync<PagedResponse<NearbyLocationResponse>>(
            "/api/locations/nearby?latitude=10&longitude=106&radiusKm=10&service=charging");
        Assert.NotNull(response);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal(factory.ActiveLocationId, Assert.Single(response.Items).Location.Id);
        Assert.InRange(response.Items[0].DistanceKm, 0, 0.01);

        var swap = await client.GetFromJsonAsync<PagedResponse<NearbyLocationResponse>>(
            "/api/locations/nearby?latitude=12&longitude=108&radiusKm=10&service=swap");
        Assert.NotNull(swap);
        Assert.Equal("B Station", Assert.Single(swap.Items).Location.Name);

        var hidden = await client.GetFromJsonAsync<PagedResponse<NearbyLocationResponse>>(
            "/api/locations/nearby?latitude=11&longitude=107&radiusKm=10");
        Assert.NotNull(hidden);
        Assert.Equal(0, hidden.TotalCount);

        var search = await client.GetFromJsonAsync<PagedResponse<NearbyLocationResponse>>(
            "/api/locations/nearby?latitude=10&longitude=106&radiusKm=10&search=address%20a");
        Assert.NotNull(search);
        Assert.Equal(1, search.TotalCount);
    }

    [Theory]
    [InlineData("longitude=106")]
    [InlineData("latitude=10&longitude=106&radiusKm=0")]
    [InlineData("latitude=10&longitude=106&service=invalid")]
    [InlineData("latitude=10&longitude=106&availability=invalid")]
    [InlineData("latitude=10&longitude=106&page=2147483647&pageSize=100")]
    [InlineData("latitude=10&longitude=106&radiusKm=101")]
    public async Task Nearby_rejects_invalid_queries(string query)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync($"/api/locations/nearby?{query}")).StatusCode);
    }

    [Fact]
    public async Task Only_admin_can_create_update_and_hide_a_location()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var input = new LocationUpsertRequest
        {
            Name = "New Station", Address = "Test address", Latitude = 13.766667m,
            Longitude = 109.233333m, OffersCarCharging = true,
            OperatingStatus = LocationOperatingStatus.Maintenance,
            IsOpen24Hours = true, ChargingPowerKw = 150,
            AvailableChargingBays = 6
        };
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/locations", input)).StatusCode);

        var email = $"admin-{Guid.NewGuid():N}@example.test";
        await factory.CreateAdminAsync(email, "StrongPass123");
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = email, Password = "StrongPass123" });
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var created = await client.PostAsJsonAsync("/api/locations", input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("\"operatingStatus\":\"Maintenance\"", await created.Content.ReadAsStringAsync());
        var location = await created.Content.ReadFromJsonAsync<LocationResponse>();
        Assert.NotNull(location);
        Assert.Equal(LocationOperatingStatus.Maintenance, location.OperatingStatus);
        Assert.True(location.IsOpen24Hours);
        Assert.Equal(150, location.ChargingPowerKw);
        Assert.Equal(6, location.AvailableChargingBays);
        var publicDetail = await client.GetFromJsonAsync<LocationResponse>($"/api/locations/{location.Id}");
        Assert.Equal(LocationOperatingStatus.Maintenance, publicDetail?.OperatingStatus);
        Assert.Equal(6, publicDetail?.AvailableChargingBays);

        var update = await client.PutAsJsonAsync($"/api/locations/{location.Id}", new LocationUpsertRequest
        {
            Name = "Updated Station", Address = "Updated address", Latitude = 13.77m,
            Longitude = 109.22m, OffersBatterySwap = true,
            OperatingStatus = LocationOperatingStatus.Closed,
            IsOpen24Hours = true, BatteryCapacityKwh = 1.5m,
            AvailableBatteries = 12
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<LocationResponse>();
        Assert.Equal(LocationOperatingStatus.Closed, updated?.OperatingStatus);
        Assert.Null(updated?.ChargingPowerKw);
        Assert.Equal(1.5m, updated?.BatteryCapacityKwh);
        Assert.Equal(12, updated?.AvailableBatteries);
        var publicList = await client.GetFromJsonAsync<PagedResponse<LocationResponse>>(
            "/api/locations?pageSize=100");
        Assert.Contains(publicList!.Items, x => x.Id == location.Id &&
            x.OperatingStatus == LocationOperatingStatus.Closed);

        var hidden = await client.PatchAsJsonAsync($"/api/locations/{location.Id}/visibility",
            new LocationVisibilityRequest { IsActive = false });
        Assert.Equal(HttpStatusCode.NoContent, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/locations/{location.Id}")).StatusCode);
        var adminList = await client.GetFromJsonAsync<PagedResponse<AdminLocationResponse>>(
            "/api/locations/admin?pageSize=100");
        Assert.NotNull(adminList);
        Assert.Contains(adminList.Items, x => x.Location.Id == location.Id && !x.IsActive);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/locations", new
            {
                name = "Missing coordinates", address = "Test", offersCarCharging = true
            })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/locations", new
            {
                name = "Invalid status", address = "Test", latitude = 10,
                longitude = 106, offersCarCharging = true, operatingStatus = "Faulted"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/locations", new
            {
                name = "Invalid battery count", address = "Test", latitude = 10,
                longitude = 106, offersBatterySwap = true, availableBatteries = 25
            })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/locations", new
            {
                name = "Invalid bay count", address = "Test", latitude = 10,
                longitude = 106, offersCarCharging = true, availableChargingBays = 15
            })).StatusCode);
    }
}
