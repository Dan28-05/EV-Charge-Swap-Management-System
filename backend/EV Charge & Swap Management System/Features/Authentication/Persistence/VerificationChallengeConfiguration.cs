using EVChargeSwap.Features.Authentication.Entities;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVChargeSwap.Features.Authentication.Persistence;

public sealed class VerificationChallengeConfiguration : IEntityTypeConfiguration<VerificationChallenge>
{
    public void Configure(EntityTypeBuilder<VerificationChallenge> builder)
    {
        builder.ToTable("verification_challenges");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(10);
        builder.Property(x => x.Destination).HasColumnName("destination").HasMaxLength(256);
        builder.Property(x => x.CodeHash).HasColumnName("code_hash").HasMaxLength(64);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(x => x.ConsumedAtUtc).HasColumnName("consumed_at_utc");
        builder.Property(x => x.FailedAttempts).HasColumnName("failed_attempts");
        builder.Property(x => x.ConcurrencyStamp).HasColumnName("concurrency_stamp").IsConcurrencyToken();
        builder.HasIndex(x => new { x.UserId, x.Channel, x.CreatedAtUtc });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
