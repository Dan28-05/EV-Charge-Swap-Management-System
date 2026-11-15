using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EVChargeSwap.Features.Authentication.Requests;
using EVChargeSwap.Features.Authentication.Responses;
using EVChargeSwap.Features.Users.Requests;
using EVChargeSwap.Features.Users.Responses;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class AuthenticationApiTests : IClassFixture<ApiFactory>
{
    private const string ValidPassword = "StrongPass123";
    private readonly ApiFactory factory;
    private readonly HttpClient client;

    public AuthenticationApiTests(ApiFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
    }

    [Fact]
    public async Task Customer_can_register_and_read_their_profile()
    {
        var auth = await RegisterAsync();
        Assert.Equal("Bearer", auth.TokenType);
        Assert.Contains("Customer", auth.User.Roles);
        Assert.NotEmpty(auth.AccessToken);
        Assert.NotEmpty(auth.RefreshToken);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var profile = await client.GetFromJsonAsync<UserResponse>("/api/users/me");
        Assert.NotNull(profile);
        Assert.Equal(auth.User.Id, profile.Id);
        Assert.Equal(auth.User.Email, profile.Email);
    }

    [Fact]
    public async Task Duplicate_registration_is_rejected()
    {
        var email = UniqueEmail("duplicate");
        await RegisterAsync(email);
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Email = email, FullName = "Duplicate User", Password = ValidPassword });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Login_does_not_reveal_whether_the_email_or_password_is_wrong()
    {
        var email = UniqueEmail("login");
        await RegisterAsync(email);
        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = "WrongPass123" });
        var unknownEmail = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = UniqueEmail("unknown"), Password = ValidPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(), await unknownEmail.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Refresh_rotates_tokens_and_reuse_revokes_the_token_family()
    {
        var original = await RegisterAsync();
        var firstRefresh = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = original.RefreshToken });
        firstRefresh.EnsureSuccessStatusCode();
        var rotated = await firstRefresh.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(rotated);
        Assert.NotEqual(original.RefreshToken, rotated.RefreshToken);

        var reuse = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = original.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var familyWasRevoked = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, familyWasRevoked.StatusCode);
    }

    [Fact]
    public async Task Logout_is_idempotent_and_revokes_the_refresh_token()
    {
        var auth = await RegisterAsync();
        var request = new RefreshTokenRequest { RefreshToken = auth.RefreshToken };
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/logout", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/logout", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", request)).StatusCode);
    }

    [Fact]
    public async Task Only_admin_can_create_an_operator()
    {
        var customer = await RegisterAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.AccessToken);
        var forbidden = await client.PostAsJsonAsync("/api/users/operators", NewOperator());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var adminEmail = UniqueEmail("admin");
        await factory.CreateAdminAsync(adminEmail, ValidPassword);
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = adminEmail, Password = ValidPassword });
        login.EnsureSuccessStatusCode();
        var admin = await login.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var created = await client.PostAsJsonAsync("/api/users/operators", NewOperator());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var operatorUser = await created.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(operatorUser);
        Assert.Contains("Operator", operatorUser.Roles);
    }

    private async Task<AuthenticationResponse> RegisterAsync(string? email = null)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email ?? UniqueEmail("customer"),
            FullName = "Test Customer",
            PhoneNumber = "+84901234567",
            Password = ValidPassword
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private static CreateOperatorRequest NewOperator() => new()
    {
        Email = UniqueEmail("operator"),
        FullName = "Test Operator",
        Password = ValidPassword
    };

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";
}
