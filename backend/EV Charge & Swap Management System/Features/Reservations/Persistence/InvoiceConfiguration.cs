using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVChargeSwap.Features.Reservations.Persistence;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");
        builder.HasKey(x => x.Id);

        builder.HasOne<Reservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.TotalKwh).HasPrecision(12, 3);
        builder.Property(x => x.KwhRate).HasPrecision(12, 2);
        builder.Property(x => x.SwapRate).HasPrecision(8, 2);
        builder.Property(x => x.EnergyAmount).HasPrecision(15, 2);
        builder.Property(x => x.TotalAmount).HasPrecision(15, 2);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.PaymentReference).HasMaxLength(64);

        builder.HasIndex(x => x.ReservationId).IsUnique();
        builder.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc });
        builder.HasIndex(x => x.Status);
    }
}
