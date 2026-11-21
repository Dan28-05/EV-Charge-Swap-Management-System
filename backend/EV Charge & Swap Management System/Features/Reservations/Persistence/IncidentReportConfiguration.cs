using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVChargeSwap.Features.Reservations.Persistence;

internal sealed class IncidentReportConfiguration : IEntityTypeConfiguration<IncidentReport>
{
    public void Configure(EntityTypeBuilder<IncidentReport> builder)
    {
        builder.ToTable("incident_reports", table =>
        {
            table.HasCheckConstraint("ck_incidents_resource", """("ChargingBayId" IS NULL) <> ("BatteryId" IS NULL)""");
            table.HasCheckConstraint("ck_incidents_resolution", """
                (("Resolution" IS NULL AND "ResolvedAtUtc" IS NULL AND "ResolvedById" IS NULL)
                    OR ("Resolution" IN ('Reassigned', 'Cancelled', 'Recorded')
                        AND "Resolution" IS NOT NULL AND "ResolvedAtUtc" IS NOT NULL AND "ResolvedById" IS NOT NULL))
                AND (CASE WHEN "Resolution" = 'Reassigned' THEN
                    ("ChargingBayId" IS NOT NULL AND "ReplacementChargingBayId" IS NOT NULL AND "ReplacementBatteryId" IS NULL)
                    OR ("BatteryId" IS NOT NULL AND "ReplacementBatteryId" IS NOT NULL AND "ReplacementChargingBayId" IS NULL)
                    ELSE "ReplacementChargingBayId" IS NULL AND "ReplacementBatteryId" IS NULL END)
                """);
        });
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Resolution).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => new { x.LocationId, x.ResolvedAtUtc, x.ReportedAtUtc });
        builder.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChargingBay>().WithMany().HasForeignKey(x => x.ChargingBayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Battery>().WithMany().HasForeignKey(x => x.BatteryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChargingBay>().WithMany().HasForeignKey(x => x.ReplacementChargingBayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Battery>().WithMany().HasForeignKey(x => x.ReplacementBatteryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Reservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChargingSession>().WithMany().HasForeignKey(x => x.ChargingSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReportedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ResolvedById).OnDelete(DeleteBehavior.Restrict);
    }
}
