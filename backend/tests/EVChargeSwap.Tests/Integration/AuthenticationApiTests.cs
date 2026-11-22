using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EVChargeSwap.Features.Authentication.Requests;
using EVChargeSwap.Features.Authentication.Responses;
using EVChargeSwap.Features.Authentication.Services;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task Customer_can_update_own_name_without_changing_login_channels()
    {
        var auth = await RegisterAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var response = await client.PutAsJsonAsync("/api/users/me", new UpdateProfileRequest { FullName = "Nguyen Van A" });
        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(profile);
        Assert.Equal("Nguyen Van A", profile.FullName);
        Assert.Equal(auth.User.Email, profile.Email);
        Assert.Equal(auth.User.PhoneNumber, profile.PhoneNumber);
    }

    [Fact]
    public async Task Changing_password_requires_current_password_and_revokes_old_session()
    {
        var auth = await RegisterAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/users/me/change-password",
            new ChangePasswordRequest { CurrentPassword = "WrongPass123", NewPassword = "BetterPass123" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/users/me/change-password",
            new ChangePasswordRequest { CurrentPassword = ValidPassword, NewPassword = "BetterPass123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = auth.RefreshToken })).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = auth.User.Email, Password = ValidPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = auth.User.Email, Password = "BetterPass123" })).StatusCode);
    }

    [Fact]
    public async Task Password_reset_code_is_single_use_and_invalidates_old_session()
    {
        var auth = await RegisterAsync();
        var unknown = await client.PostAsJsonAsync("/api/auth/password/forgot",
            new ForgotPasswordRequest { Identifier = UniqueEmail("unknown") });
        var sent = await client.PostAsJsonAsync("/api/auth/password/forgot",
            new ForgotPasswordRequest { Identifier = auth.User.Email });
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        var code = factory.Services.GetRequiredService<DevelopmentCodeInbox>().GetCode("email", auth.User.Email);
        Assert.NotNull(code);
        var reset = new ResetPasswordRequest
        {
            Identifier = auth.User.Email, Code = code, NewPassword = "BetterPass123"
        };
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/password/reset", reset)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/password/reset", reset)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = auth.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task Password_reset_accepts_a_verified_phone_number()
    {
        var auth = await RegisterAsync();
        var phone = auth.User.PhoneNumber!;
        var registrationCode = factory.Services.GetRequiredService<DevelopmentCodeInbox>().GetCode("phone", phone);
        Assert.NotNull(registrationCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/verification/confirm",
            new VerifyContactRequest { Email = auth.User.Email, Channel = "phone", Code = registrationCode })).StatusCode);

        var localPhone = "0" + phone[3..];
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/password/forgot",
            new ForgotPasswordRequest { Identifier = localPhone })).StatusCode);
        var resetCode = factory.Services.GetRequiredService<DevelopmentCodeInbox>().GetCode("phone", phone);
        Assert.NotNull(resetCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/password/reset",
            new ResetPasswordRequest { Identifier = localPhone, Code = resetCode, NewPassword = "BetterPass123" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = phone, Password = "BetterPass123" })).StatusCode);
    }

    [Fact]
    public async Task Customer_can_verify_each_contact_from_their_profile()
    {
        var auth = await RegisterAsync();
        Assert.True(auth.User.EmailConfirmed);
        Assert.False(auth.User.PhoneNumberConfirmed);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(
            "/api/auth/verification/me/send", new { channel = "phone" })).StatusCode);
        var code = factory.Services.GetRequiredService<DevelopmentCodeInbox>()
            .GetCode("phone", auth.User.PhoneNumber!);
        Assert.NotNull(code);
        var wrongCode = code == "000000" ? "111111" : "000000";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/auth/verification/me/confirm", new { channel = "phone", code = wrongCode })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(
            "/api/auth/verification/me/confirm", new { channel = "phone", code })).StatusCode);

        var profile = await client.GetFromJsonAsync<UserResponse>("/api/users/me");
        Assert.NotNull(profile);
        Assert.True(profile.EmailConfirmed);
        Assert.True(profile.PhoneNumberConfirmed);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(
            "/api/auth/verification/me/send", new { channel = "phone" })).StatusCode);
    }

    [Fact]
    public async Task Duplicate_registration_is_rejected()
    {
        var email = UniqueEmail("duplicate");
        await RegisterAsync(email);
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Email = email, FullName = "Duplicate User", PhoneNumber = UniquePhone(), Password = ValidPassword });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Login_does_not_reveal_whether_the_email_or_password_is_wrong()
    {
        var email = UniqueEmail("login");
        await RegisterAsync(email);
        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = email, Password = "WrongPass123" });
        var unknownEmail = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = UniqueEmail("unknown"), Password = ValidPassword });

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
            new LoginRequest { Identifier = adminEmail, Password = ValidPassword });
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

    [Fact]
    public async Task Customer_must_verify_the_chosen_login_channel()
    {
        var email = UniqueEmail("verify");
        var phone = UniquePhone();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email, PhoneNumber = phone, FullName = "Verification User", Password = ValidPassword
        });
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);

        var emailLogin = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = email, Password = ValidPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, emailLogin.StatusCode);

        var code = factory.Services.GetRequiredService<DevelopmentCodeInbox>().GetCode("phone", phone);
        Assert.NotNull(code);
        var confirmation = await client.PostAsJsonAsync("/api/auth/verification/confirm",
            new VerifyContactRequest { Email = email, Channel = "phone", Code = code });
        Assert.Equal(HttpStatusCode.NoContent, confirmation.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/verification/confirm",
            new VerifyContactRequest { Email = email, Channel = "phone", Code = code })).StatusCode);

        var phoneLogin = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = phone, Password = ValidPassword });
        phoneLogin.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = email, Password = ValidPassword })).StatusCode);
    }

    [Fact]
    public async Task Refresh_keeps_the_original_thirty_day_deadline()
    {
        var original = await RegisterAsync();
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = original.RefreshToken });
        refresh.EnsureSuccessStatusCode();
        var rotated = await refresh.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(rotated);
        Assert.Equal(original.RefreshTokenExpiresAtUtc, rotated.RefreshTokenExpiresAtUtc);
        Assert.InRange(original.RefreshTokenExpiresAtUtc - DateTime.UtcNow,
            TimeSpan.FromDays(29), TimeSpan.FromDays(30));
    }

    [Fact]
    public async Task Verification_rejects_wrong_codes_and_duplicate_phone()
    {
        var email = UniqueEmail("codes");
        var phone = UniquePhone();
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Email = email, PhoneNumber = phone, FullName = "Code User", Password = ValidPassword })).StatusCode);
        var correct = factory.Services.GetRequiredService<DevelopmentCodeInbox>().GetCode("phone", phone);
        Assert.NotNull(correct);
        var wrong = correct == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/verification/confirm",
                new VerifyContactRequest { Email = email, Channel = "phone", Code = wrong })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/verification/confirm",
            new VerifyContactRequest { Email = email, Channel = "phone", Code = correct })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Email = UniqueEmail("other"), PhoneNumber = phone, FullName = "Other", Password = ValidPassword })).StatusCode);
    }

    [Fact]
    public async Task Invalid_non_email_login_does_not_match_or_lock_out_accounts_without_phone()
    {
        using var isolatedFactory = new ApiFactory();
        using var isolatedClient = isolatedFactory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var adminEmail = UniqueEmail("admin-nophone");
        await isolatedFactory.CreateAdminAsync(adminEmail, ValidPassword);

        // 6 failed login attempts with an invalid non-email identifier must return 401 and never lock out Admin
        for (var i = 0; i < 6; i++)
        {
            var bad = await isolatedClient.PostAsJsonAsync("/api/auth/login",
                new LoginRequest { Identifier = "not-a-phone", Password = "WrongPassword123" });
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }

        // Admin can still log in immediately with valid credentials
        var adminLogin = await isolatedClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = adminEmail, Password = ValidPassword });
        adminLogin.EnsureSuccessStatusCode();

        // Even with a second account having PhoneNumber == null, invalid identifier must return 401 (never 500)
        await isolatedFactory.CreateAdminAsync(UniqueEmail("admin2-nophone"), ValidPassword);
        var badMulti = await isolatedClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = "invalid-id", Password = ValidPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, badMulti.StatusCode);

        // Resending phone verification for an account without a phone must return 202 (never 500)
        var resend = await isolatedClient.PostAsJsonAsync("/api/auth/verification/resend",
            new ResendVerificationRequest { Email = adminEmail, Channel = "phone" });
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
    }

    [Fact]
    public async Task Auth_rate_limiter_partitions_case_insensitively()
    {
        using var isolatedFactory = new ApiFactory();
        using var isolatedClient = isolatedFactory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        HttpStatusCode lastStatus = HttpStatusCode.OK;
        for (var i = 0; i < 31; i++)
        {
            var path = i % 2 == 0 ? "/api/auth/login" : "/api/auth/LOGIN";
            var response = await isolatedClient.PostAsJsonAsync(path,
                new LoginRequest { Identifier = "nobody@example.test", Password = "WrongPassword123" });
            lastStatus = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, lastStatus);
    }

    private async Task<AuthenticationResponse> RegisterAsync(string? email = null)
    {
        email ??= UniqueEmail("customer");
        var phone = UniquePhone();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            FullName = "Test Customer",
            PhoneNumber = phone,
            Password = ValidPassword
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var code = factory.Services.GetRequiredService<DevelopmentCodeInbox>().GetCode("email", email);
        Assert.NotNull(code);
        var confirm = await client.PostAsJsonAsync("/api/auth/verification/confirm",
            new VerifyContactRequest { Email = email, Channel = "email", Code = code });
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Identifier = email, Password = ValidPassword });
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private static CreateOperatorRequest NewOperator() => new()
    {
        Email = UniqueEmail("operator"),
        FullName = "Test Operator",
        Password = ValidPassword
    };

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";
    private static string UniquePhone() => "+849" + Random.Shared.NextInt64(10000000, 99999999).ToString("D8");
}
