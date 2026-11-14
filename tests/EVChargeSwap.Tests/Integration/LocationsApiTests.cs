using System.Net;
using System.Net.Http.Json;
using EVChargeSwap.Common.Pagination;
using EVChargeSwap.Features.Locations;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class LocationsApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory factory;
    private readonly HttpClient client;

    public LocationsApiTests(ApiFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
    }

    [Fact]
    public async Task List_is_sorted_paginated_and_excludes_inactive_locations()
    {
        var first = await client.GetFromJsonAsync<PagedResponse<LocationResponse>>("/api/locations?page=1&pageSize=1");
        var second = await client.GetFromJsonAsync<PagedResponse<LocationResponse>>("/api/locations?page=2&pageSize=1");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(2, first.TotalCount);
        Assert.Equal("A Station", Assert.Single(first.Items).Name);
        Assert.Equal("B Station", Assert.Single(second.Items).Name);
    }

    [Fact]
    public async Task Detail_returns_the_public_response()
    {
        var result = await client.GetFromJsonAsync<LocationResponse>($"/api/locations/{factory.ActiveLocationId}");
        Assert.NotNull(result);
        Assert.Equal("A Station", result.Name);
        Assert.True(result.OffersCarCharging);
    }

    [Fact]
    public async Task Inactive_and_missing_locations_are_not_public()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/locations/{factory.InactiveLocationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/locations/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    public async Task Invalid_queries_return_validation_problem_details(string query)
    {
        var response = await client.GetAsync($"/api/locations?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
        Assert.True(response.Headers.Contains("X-Request-ID"));
    }
}
