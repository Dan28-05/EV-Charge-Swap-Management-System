using EVChargeSwap.Data.Entities;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVChargeSwap.Data.Persistence;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(x => x.Id);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ActionUrl).HasMaxLength(500);

        builder.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc, x.Id });
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Action).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(64);
        builder.Property(x => x.ResourceId).HasMaxLength(64);

        builder.HasIndex(x => new { x.TimestampUtc, x.Action });
        builder.HasIndex(x => x.ActorId);
    }
}
