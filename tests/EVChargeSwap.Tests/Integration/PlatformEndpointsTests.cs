using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class PlatformEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory factory;
    private readonly HttpClient client;

    public PlatformEndpointsTests(ApiFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_returns_healthy_when_test_database_is_available(string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Readiness_reports_failure_but_liveness_still_succeeds()
    {
        using var failingFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration("database",
                    _ => new FailedDatabaseCheck(), HealthStatus.Unhealthy, ["ready"]));
            })));
        using var failingClient = failingFactory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await failingClient.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await failingClient.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Scalar_and_openapi_are_available_in_development()
    {
        var scalar = await client.GetAsync("/scalar/v1");
        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
        Assert.Equal("text/html", scalar.Content.Headers.ContentType?.MediaType);
        Assert.Contains("EV Charge & Swap API", await scalar.Content.ReadAsStringAsync());
        var openapi = await client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/locations", openapi);
    }

    [Fact]
    public async Task Scalar_and_openapi_are_not_exposed_in_production()
    {
        using var productionFactory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var productionClient = productionFactory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.NotFound, (await productionClient.GetAsync("/scalar/v1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await productionClient.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public async Task Unexpected_errors_hide_details_and_include_a_matching_request_id()
    {
        var response = await client.GetAsync("/test-only/errors");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("test-only-sensitive-detail", content);
        using var json = JsonDocument.Parse(content);
        Assert.Equal(response.Headers.GetValues("X-Request-ID").Single(),
            json.RootElement.GetProperty("traceId").GetString());
    }

    private sealed class FailedDatabaseCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Unhealthy());
    }
}
