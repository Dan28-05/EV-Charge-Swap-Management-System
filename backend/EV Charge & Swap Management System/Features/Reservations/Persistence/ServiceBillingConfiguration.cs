using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVChargeSwap.Features.Reservations.Persistence;

internal sealed class ServiceBillingConfiguration : IEntityTypeConfiguration<Tariff>,
    IEntityTypeConfiguration<BatterySwap>, IEntityTypeConfiguration<Payment>, IEntityTypeConfiguration<PaymentWebhookEvent>
{
    public void Configure(EntityTypeBuilder<Tariff> builder)
    {
        builder.ToTable("tariffs", table => table.HasCheckConstraint("ck_tariffs_price", """ "UnitPrice" >= 0 """));
        builder.Property(x => x.UnitPrice).HasPrecision(12, 2);
        builder.HasIndex(x => new { x.LocationId, x.ServiceType, x.EffectiveFromUtc }).IsUnique();
        builder.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<BatterySwap> builder)
    {
        builder.ToTable("battery_swaps", table =>
            table.HasCheckConstraint("ck_battery_swaps_distinct", """ "ReturnedBatteryId" <> "IssuedBatteryId" """));
        builder.HasOne<Reservation>().WithOne().HasForeignKey<BatterySwap>(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Battery>().WithMany().HasForeignKey(x => x.ReturnedBatteryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Battery>().WithMany().HasForeignKey(x => x.IssuedBatteryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.InspectionResult).HasMaxLength(500);
        builder.Property(x => x.AppliedRate).HasPrecision(12, 2);
    }

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", table => table.HasCheckConstraint("ck_payments_amount", """ "Amount" >= 0 """));
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CollectedByOperatorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Amount).HasPrecision(15, 2);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        builder.HasIndex(x => x.InvoiceId);
        builder.HasIndex(x => x.InvoiceId, "ux_payments_pending").IsUnique().HasFilter(""" "Status" = 'Pending' """);
        builder.HasIndex(x => x.InvoiceId, "ux_payments_succeeded").IsUnique().HasFilter(""" "Status" = 'Succeeded' """);
        builder.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<PaymentWebhookEvent> builder)
    {
        builder.ToTable("payment_webhook_events");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PayloadHash).HasMaxLength(64);
        builder.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}
