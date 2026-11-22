using EVChargeSwap.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVChargeSwap.Data.Persistence;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RoutingKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.TraceParent).HasMaxLength(256);

        builder.HasIndex(x => new { x.ProcessedAtUtc, x.CreatedAtUtc })
            .HasFilter("\"ProcessedAtUtc\" IS NULL");
        builder.HasIndex(x => new { x.RetryCount, x.CreatedAtUtc }, "ix_outbox_pending_retry")
            .HasFilter("\"ProcessedAtUtc\" IS NULL");
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        builder.HasKey(x => new { x.Id, x.ConsumerName });
        builder.Property(x => x.ConsumerName).HasMaxLength(128).IsRequired();
    }
}
