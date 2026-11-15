using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EVChargeSwap.Features.Authentication.Entities;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EVChargeSwap.Features.Authentication.Services;

public sealed class JwtTokenService(IOptions<JwtOptions> jwtOptions, TimeProvider timeProvider)
{
    private readonly JwtOptions options = jwtOptions.Value;

    public IssuedAccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = now.AddMinutes(options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new("security_stamp", user.SecurityStamp ?? string.Empty)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(options.Issuer, options.Audience, claims,
            notBefore: now, expires: expiresAtUtc, signingCredentials: credentials);

        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }

    public IssuedRefreshToken CreateRefreshToken(Guid userId, Guid? familyId = null, DateTime? sessionExpiresAtUtc = null)
    {
        var rawToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(64));
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entity = new RefreshToken(userId, familyId ?? Guid.NewGuid(), HashRefreshToken(rawToken),
            now, sessionExpiresAtUtc ?? now.AddDays(options.RefreshTokenDays));
        return new IssuedRefreshToken(rawToken, entity);
    }

    public static string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed record IssuedAccessToken(string Value, DateTime ExpiresAtUtc);
public sealed record IssuedRefreshToken(string Value, RefreshToken Entity);
