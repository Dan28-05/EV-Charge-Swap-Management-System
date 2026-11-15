using System.Security.Cryptography;
using System.Text;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authentication.Entities;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVChargeSwap.Features.Authentication.Services;

public sealed class VerificationService(
    AppDbContext db, UserManager<ApplicationUser> users,
    IVerificationCodeSender sender, IOptions<JwtOptions> jwt,
    TimeProvider clock)
{
    public bool DeliveryAvailable => sender is not UnconfiguredVerificationSender;
    public static bool ValidChannel(string channel) => channel is "email" or "phone";

    public async Task SendAsync(ApplicationUser user, string channel, CancellationToken ct)
    {
        if (!ValidChannel(channel)) throw new ArgumentException("Invalid channel.", nameof(channel));
        var destination = channel == "email" ? user.Email! : user.PhoneNumber!;
        var now = clock.GetUtcNow().UtcDateTime;
        var pending = await db.VerificationChallenges
            .Where(x => x.UserId == user.Id && x.Channel == channel && x.ConsumedAtUtc == null)
            .ToListAsync(ct);
        if (pending.Any(x => x.CreatedAtUtc > now.AddSeconds(-60))) return;
        foreach (var old in pending) old.Consume(now);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var challenge = new VerificationChallenge(user.Id, channel, destination,
            Hash(user.Id, channel, code), now);
        db.VerificationChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        await sender.SendAsync(channel, destination, code, ct);
    }

    public async Task<bool> VerifyAsync(ApplicationUser user, string channel, string code, CancellationToken ct)
    {
        if (!ValidChannel(channel)) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        var challenge = await db.VerificationChallenges
            .Where(x => x.UserId == user.Id && x.Channel == channel)
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (challenge is null || !challenge.IsAvailable(now)) return false;

        var expected = Convert.FromHexString(challenge.CodeHash);
        var actual = Convert.FromHexString(Hash(user.Id, channel, code));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            challenge.Fail();
            await db.SaveChangesAsync(ct);
            return false;
        }

        challenge.Consume(now);
        if (channel == "email") user.EmailConfirmed = true;
        else user.PhoneNumberConfirmed = true;
        try
        {
            var updated = await users.UpdateAsync(user);
            if (!updated.Succeeded) return false;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private string Hash(Guid userId, string channel, string code)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.SigningKey + ":verification"));
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{userId}:{channel}:{code}")));
    }
}
