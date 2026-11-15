using System.Data;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Auth.Requests;
using EVChargeSwap.Features.Auth.Responses;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Auth;

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AppDbContext db,
    TokenService tokenService,
    TimeProvider timeProvider)
{
    public async Task<AuthResult<AuthResponse>> RegisterAsync(RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
            return AuthResult<AuthResponse>.Fail(AuthFailure.Conflict, "An account with this email already exists.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var user = new ApplicationUser(email, request.FullName,
            timeProvider.GetUtcNow().UtcDateTime)
        {
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim()
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
            return AuthResult<AuthResponse>.Fail(AuthFailure.Validation, "Registration failed.", ToErrors(createResult));

        var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Customer);
        if (!roleResult.Succeeded)
            return AuthResult<AuthResponse>.Fail(AuthFailure.Validation, "Could not assign the customer role.", ToErrors(roleResult));

        var response = await IssueTokensAsync(user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AuthResult<AuthResponse>.Success(response);
    }

    public async Task<AuthResult<AuthResponse>> LoginAsync(LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
            return InvalidCredentials();

        var signInResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!signInResult.Succeeded)
            return InvalidCredentials();

        return AuthResult<AuthResponse>.Success(await IssueTokensAsync(user, cancellationToken));
    }

    public async Task<AuthResult<AuthResponse>> RefreshAsync(string rawRefreshToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = TokenService.HashRefreshToken(rawRefreshToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var storedToken = await db.RefreshTokens.Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (storedToken is null)
            return InvalidRefreshToken();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (storedToken.RevokedAtUtc is not null)
        {
            if (storedToken.ReplacedByTokenHash is not null)
            {
                var activeFamilyTokens = await db.RefreshTokens
                    .Where(x => x.UserId == storedToken.UserId && x.FamilyId == storedToken.FamilyId && x.RevokedAtUtc == null)
                    .ToListAsync(cancellationToken);
                foreach (var token in activeFamilyTokens.Where(x => x.ExpiresAtUtc > now))
                    token.Revoke(now, "reuse_detected");
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            return InvalidRefreshToken();
        }

        if (!storedToken.IsActive(now) || !storedToken.User.IsActive)
            return InvalidRefreshToken();

        var replacement = tokenService.CreateRefreshToken(storedToken.UserId, storedToken.FamilyId);
        storedToken.Revoke(now, "rotated", replacement.Entity.TokenHash);
        db.RefreshTokens.Add(replacement.Entity);
        var roles = await userManager.GetRolesAsync(storedToken.User);
        var accessToken = tokenService.CreateAccessToken(storedToken.User, roles);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvalidRefreshToken();
        }

        return AuthResult<AuthResponse>.Success(ToAuthResponse(
            storedToken.User, roles, accessToken, replacement));
    }

    public async Task LogoutAsync(string rawRefreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = TokenService.HashRefreshToken(rawRefreshToken);
        var storedToken = await db.RefreshTokens.SingleOrDefaultAsync(
            x => x.TokenHash == tokenHash, cancellationToken);
        if (storedToken is null) return;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (storedToken.IsActive(now))
        {
            storedToken.Revoke(now, "logout");
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Logout is idempotent; another request already changed the same token.
            }
        }
    }

    public async Task<AuthResult<UserResponse>> GetUserAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
            return AuthResult<UserResponse>.Fail(AuthFailure.NotFound, "User was not found.");
        var roles = await userManager.GetRolesAsync(user);
        return AuthResult<UserResponse>.Success(ToUserResponse(user, roles));
    }

    public async Task<AuthResult<UserResponse>> CreateOperatorAsync(CreateOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
            return AuthResult<UserResponse>.Fail(AuthFailure.Conflict, "An account with this email already exists.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var user = new ApplicationUser(email, request.FullName,
            timeProvider.GetUtcNow().UtcDateTime)
        {
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim()
        };
        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
            return AuthResult<UserResponse>.Fail(AuthFailure.Validation, "Operator creation failed.", ToErrors(createResult));
        var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Operator);
        if (!roleResult.Succeeded)
            return AuthResult<UserResponse>.Fail(AuthFailure.Validation, "Could not assign the operator role.", ToErrors(roleResult));
        await transaction.CommitAsync(cancellationToken);
        return AuthResult<UserResponse>.Success(ToUserResponse(user, [AppRoles.Operator]));
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var accessToken = tokenService.CreateAccessToken(user, roles);
        var refreshToken = tokenService.CreateRefreshToken(user.Id);
        db.RefreshTokens.Add(refreshToken.Entity);
        await db.SaveChangesAsync(cancellationToken);
        return ToAuthResponse(user, roles, accessToken, refreshToken);
    }

    private static AuthResponse ToAuthResponse(ApplicationUser user, IEnumerable<string> roles,
        IssuedAccessToken accessToken, IssuedRefreshToken refreshToken) =>
        new("Bearer", accessToken.Value, accessToken.ExpiresAtUtc, refreshToken.Value,
            refreshToken.Entity.ExpiresAtUtc, ToUserResponse(user, roles));

    private static UserResponse ToUserResponse(ApplicationUser user, IEnumerable<string> roles) =>
        new(user.Id, user.Email ?? string.Empty, user.FullName, user.PhoneNumber, user.IsActive, roles.ToArray());

    private static IReadOnlyDictionary<string, string[]> ToErrors(IdentityResult result) =>
        result.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray());

    private static AuthResult<AuthResponse> InvalidCredentials() =>
        AuthResult<AuthResponse>.Fail(AuthFailure.Unauthorized, "Invalid email or password.");

    private static AuthResult<AuthResponse> InvalidRefreshToken() =>
        AuthResult<AuthResponse>.Fail(AuthFailure.Unauthorized, "The refresh token is invalid or expired.");
}
