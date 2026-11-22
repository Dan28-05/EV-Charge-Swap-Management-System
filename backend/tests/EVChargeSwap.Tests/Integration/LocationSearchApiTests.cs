using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EVChargeSwap.Common.Messaging.Consumers;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Common.Pagination;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authentication.Requests;
using EVChargeSwap.Features.Authentication.Responses;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Locations.Requests;
using EVChargeSwap.Features.Locations.Responses;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class LocationSearchApiTests
{
    private const string RebuildPath = "/api/locations/admin/search/rebuild";
    private const string TestEndpoint = "EV_LOCATION_SEARCH_TEST_ENDPOINT";

    [Fact]
    public async Task Elasticsearch_outage_preserves_location_updates_and_searches_without_accents()
    {
        using var factory = new ApiFactory();
        using var app = factory.WithWebHostBuilder(builder => builder
            .UseSetting("LocationSearch:Endpoint", "http://127.0.0.1:1"));
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(RebuildPath, null)).StatusCode);
        await SignInAdminAsync(app.Services, client);

        var location = await CreateStationAsync(client);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = await db.OutboxMessages.SingleAsync(x => x.RoutingKey == EventRoutingKeys.LocationUpdated);
        Assert.Contains(location.Id.ToString(), evt.Payload);

        var page = await SearchAsync(client, "vincom dong kh");
        Assert.Equal(location.Id, Assert.Single(page.Items).Id);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync(RebuildPath, null)).StatusCode);
    }

    [ElasticsearchFact]
    public async Task Elasticsearch_index_rebuild_and_event_consumer_support_prefix_rename_and_visibility()
    {
        var endpoint = Environment.GetEnvironmentVariable(TestEndpoint)!;
        var alias = $"station-test-{Guid.NewGuid():N}";
        using var factory = new ApiFactory();
        using var app = factory.WithWebHostBuilder(builder => builder
            .UseSetting("LocationSearch:Endpoint", endpoint)
            .UseSetting("LocationSearch:Alias", alias)
            // This fixture creates its single-connection SQLite schema after starting the host.
            .ConfigureServices(services => services.RemoveAll<IHostedService>()));
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var elastic = new HttpClient { BaseAddress = new Uri(endpoint) };
        await SignInAdminAsync(app.Services, client);
        var location = await CreateStationAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(RebuildPath, null)).StatusCode);
        var indexed = await elastic.GetFromJsonAsync<System.Text.Json.JsonElement>($"/{alias}/_doc/{location.Id}");
        Assert.Equal(location.Name, indexed.GetProperty("_source").GetProperty("name").GetString());
        Assert.Equal(location.Id, Assert.Single((await SearchAsync(client, "Vincom Đồng Kh")).Items).Id);
        Assert.Equal(location.Id, Assert.Single((await SearchAsync(client, "le thanh ton")).Items).Id);

        // Use a temporarily divergent search document to prove the API actually reads Elasticsearch.
        using var projected = await elastic.PutAsJsonAsync($"/{alias}/_doc/{location.Id}?refresh=true", new
        {
            id = location.Id, name = "Trạm Đà Nẵng", address = "72 Lê Thánh Tôn"
        });
        projected.EnsureSuccessStatusCode();
        Assert.Equal(location.Name, Assert.Single((await SearchAsync(client, "da na")).Items).Name);

        using var renamed = await client.PutAsJsonAsync($"/api/locations/{location.Id}", Station("Trạm Bến Thành"));
        renamed.EnsureSuccessStatusCode();
        await DispatchLocationEventsAsync(app.Services);
        using var refreshed = await elastic.PostAsync($"/{alias}/_refresh", null);
        refreshed.EnsureSuccessStatusCode();
        Assert.Empty((await SearchAsync(client, "Vincom")).Items);
        Assert.Equal(location.Id, Assert.Single((await SearchAsync(client, "ben than")).Items).Id);

        using var hidden = await client.PatchAsJsonAsync($"/api/locations/{location.Id}/visibility",
            new LocationVisibilityRequest { IsActive = false });
        hidden.EnsureSuccessStatusCode();
        Assert.Empty((await SearchAsync(client, "ben thanh")).Items);
        await DispatchLocationEventsAsync(app.Services);
        Assert.Equal(HttpStatusCode.NotFound, (await elastic.GetAsync($"/{alias}/_doc/{location.Id}")).StatusCode);
    }

    private static async Task DispatchLocationEventsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var consumer = scope.ServiceProvider.GetRequiredService<LocationUpdatedConsumer>();
        var events = await db.OutboxMessages.Where(x => x.RoutingKey == EventRoutingKeys.LocationUpdated).ToListAsync();
        foreach (var evt in events)
            await consumer.HandleAsync(evt.EventType, evt.RoutingKey, evt.Payload, evt.Id, CancellationToken.None);
    }

    private static async Task SignInAdminAsync(IServiceProvider services, HttpClient client)
    {
        var email = $"search-admin-{Guid.NewGuid():N}@example.test";
        const string password = "StrongPass123";
        await using var scope = services.CreateAsyncScope();
        var admin = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser(email, "Search Admin", DateTime.UtcNow);
        Assert.True((await admin.CreateAsync(user, password)).Succeeded);
        Assert.True((await admin.AddToRoleAsync(user, AppRoles.Admin)).Succeeded);
        using var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = email, Password = password });
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<AuthenticationResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);
    }

    private static async Task<LocationResponse> CreateStationAsync(HttpClient client)
    {
        using var result = await client.PostAsJsonAsync("/api/locations", Station("Vincom Đồng Khởi"));
        result.EnsureSuccessStatusCode();
        return (await result.Content.ReadFromJsonAsync<LocationResponse>())!;
    }

    private static LocationUpsertRequest Station(string name) => new()
    {
        Name = name, Address = "72 Lê Thánh Tôn", Latitude = 10, Longitude = 106, OffersCarCharging = true
    };

    private static async Task<PagedResponse<LocationResponse>> SearchAsync(HttpClient client, string term) =>
        (await client.GetFromJsonAsync<PagedResponse<LocationResponse>>(
            $"/api/locations/search?query={Uri.EscapeDataString(term)}"))!;

    private sealed class ElasticsearchFactAttribute : FactAttribute
    {
        public ElasticsearchFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TestEndpoint)))
                Skip = $"Set {TestEndpoint} to run against a disposable Elasticsearch instance.";
        }
    }
}
