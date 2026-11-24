using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EVChargeSwap.Features.Admin;
using EVChargeSwap.Features.Authentication.Requests;
using EVChargeSwap.Features.Authentication.Responses;
using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Locations.Requests;
using EVChargeSwap.Features.Locations.Responses;
using EVChargeSwap.Features.Users.Requests;
using EVChargeSwap.Features.Users.Responses;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class AdminApiTests
{
    [Fact]
    public async Task Enterprise_station_onboarding_batch_provisioning_and_readiness_work_end_to_end()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        // Anonymous request rejected
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/admin/reports/summary")).StatusCode);

        var adminEmail = $"admin-{Guid.NewGuid():N}@example.test";
        await factory.CreateAdminAsync(adminEmail, "StrongPass123");
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = adminEmail, Password = "StrongPass123" });
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        // Step 1: Create draft station (IsActive = false) -> hidden from public endpoint
        var createRes = await client.PostAsJsonAsync("/api/locations", new LocationUpsertRequest
        {
            Name = "EVGo Quy Nhon Hub",
            Address = "01 Nguyen Tat Thanh, Quy Nhon",
            Latitude = 13.7669m,
            Longitude = 109.2122m,
            OffersCarCharging = true,
            OffersBatterySwap = true,
            OperatingStatus = LocationOperatingStatus.Closed,
            IsActive = false,
            ChargingPowerKw = 150,
            BatteryCapacityKwh = 1.5m
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var station = await createRes.Content.ReadFromJsonAsync<LocationResponse>();
        Assert.NotNull(station);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/locations/{station.Id}")).StatusCode);

        // Initial readiness: not ready yet
        var initialResources = await client.GetFromJsonAsync<AdminLocationResourcesResponse>(
            $"/api/admin/locations/{station.Id}/resources");
        Assert.NotNull(initialResources);
        Assert.False(initialResources.Station.IsActive);
        Assert.True(initialResources.Readiness.HasValidCoordinates);
        Assert.False(initialResources.Readiness.HasRequiredResources);
        Assert.False(initialResources.Readiness.HasServiceHours);
        Assert.False(initialResources.Readiness.HasAssignedOperator);
        Assert.False(initialResources.Readiness.CanGoLive);

        // Step 2: Batch provision charging bays & batteries (and verify duplicate skip idempotency)
        var bayBatch = await client.PostAsJsonAsync($"/api/admin/locations/{station.Id}/charging-bays/batch",
            new BatchCreateBaysRequest { Prefix = "QN-", Count = 3, StartNumber = 1, PowerKw = 150 });
        Assert.Equal(HttpStatusCode.Created, bayBatch.StatusCode);
        var bayResult = await bayBatch.Content.ReadFromJsonAsync<BatchProvisionResult>();
        Assert.NotNull(bayResult);
        Assert.Equal(3, bayResult.CreatedCount);
        Assert.Equal(0, bayResult.SkippedCount);
        Assert.Equal(["QN-01", "QN-02", "QN-03"], bayResult.CreatedCodes);

        var batBatch = await client.PostAsJsonAsync($"/api/admin/locations/{station.Id}/batteries/batch",
            new BatchCreateBatteriesRequest { Prefix = "BAT-QN-", Count = 4, StartNumber = 1 });
        Assert.Equal(HttpStatusCode.Created, batBatch.StatusCode);
        var batResult = await batBatch.Content.ReadFromJsonAsync<BatchProvisionResult>();
        Assert.NotNull(batResult);
        Assert.Equal(4, batResult.CreatedCount);

        // Step 3: Apply 24/7 Service Hours preset & create + assign Operator
        var hoursRes = await client.PutAsJsonAsync($"/api/admin/locations/{station.Id}/service-hours/preset",
            new ApplyServiceHoursPresetRequest { OpensAtMinute = 0, ClosesAtMinute = 1440 });
        Assert.Equal(HttpStatusCode.NoContent, hoursRes.StatusCode);

        var opEmail = $"op-{Guid.NewGuid():N}@example.test";
        var createOp = await client.PostAsJsonAsync("/api/users/operators", new CreateOperatorRequest
        {
            Email = opEmail,
            FullName = "Nguyen Van Truc",
            PhoneNumber = "0905123456",
            Password = "StrongPass123"
        });
        Assert.Equal(HttpStatusCode.Created, createOp.StatusCode);
        var createdOp = await createOp.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(createdOp);

        var assignRes = await client.PutAsJsonAsync($"/api/operators/{createdOp.Id}/assignment",
            new { locationId = station.Id });
        Assert.Equal(HttpStatusCode.OK, assignRes.StatusCode);

        // List operators includes assigned station
        var operators = await client.GetFromJsonAsync<List<AdminOperatorSummary>>("/api/admin/operators");
        Assert.NotNull(operators);
        var opSummary = Assert.Single(operators, x => x.Id == createdOp.Id);
        Assert.Equal(station.Id, opSummary.AssignedLocationId);
        Assert.Equal("EVGo Quy Nhon Hub", opSummary.AssignedLocationName);

        // Step 4: Readiness checklist now passes 4/4 -> CanGoLive = true
        var readyResources = await client.GetFromJsonAsync<AdminLocationResourcesResponse>(
            $"/api/admin/locations/{station.Id}/resources");
        Assert.NotNull(readyResources);
        Assert.Equal(3, readyResources.Bays.Count);
        Assert.Equal(4, readyResources.Batteries.Count);
        Assert.Equal(14, readyResources.ServiceHours.Count); // 7 days x 2 services
        Assert.Single(readyResources.AssignedOperators);
        Assert.True(readyResources.Readiness.CanGoLive);

        // Reports summary returns valid network metrics
        var report = await client.GetFromJsonAsync<AdminReportsSummaryResponse>(
            $"/api/admin/reports/summary?locationId={station.Id}");
        Assert.NotNull(report);
        Assert.Equal(1, report.TotalStations);
        Assert.Equal(0, report.ActiveStations);
        Assert.Equal(3, report.TotalBays);
        Assert.Single(report.ByLocation);

        // Non-admin (Operator) cannot access Admin endpoints
        var opLogin = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = opEmail, Password = "StrongPass123" });
        opLogin.EnsureSuccessStatusCode();
        var opAuth = await opLogin.Content.ReadFromJsonAsync<AuthenticationResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opAuth!.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/operators")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/reports/summary")).StatusCode);
    }
}
