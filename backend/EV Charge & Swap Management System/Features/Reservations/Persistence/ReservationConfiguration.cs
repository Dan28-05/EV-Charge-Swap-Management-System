using EVChargeSwap.Features.Locations.Entities;
using EVChargeSwap.Features.Reservations.Entities;
using EVChargeSwap.Features.Users.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVChargeSwap.Features.Reservations.Persistence;

internal sealed class ReservationConfiguration :
    IEntityTypeConfiguration<Reservation>,
    IEntityTypeConfiguration<ChargingBay>,
    IEntityTypeConfiguration<Battery>,
    IEntityTypeConfiguration<ServiceHours>,
    IEntityTypeConfiguration<OperatorAssignment>
{
    public const string CustomerHoldIndex = "ux_reservations_active_customer";
    public const string BayHoldIndex = "ux_reservations_active_bay";
    public const string BatteryHoldIndex = "ux_reservations_active_battery";

    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations", table =>
        {
            table.HasCheckConstraint("ck_reservations_resource",
                """("ChargingBayId" IS NULL) <> ("BatteryId" IS NULL)""");
            table.HasCheckConstraint("ck_reservations_expiry", """ "ExpiresAtUtc" > "CreatedAtUtc" """);
            table.HasCheckConstraint("ck_reservations_lifecycle", """
                (("CheckedInAtUtc" IS NULL) = ("CheckedInById" IS NULL))
                AND (("VehicleArrivedAtUtc" IS NULL) = ("VehicleArrivedById" IS NULL))
                AND (("VehicleDepartedAtUtc" IS NULL) = ("VehicleDepartedById" IS NULL))
                AND (("NoChargeConfirmedAtUtc" IS NULL) = ("NoChargeConfirmedById" IS NULL))
                AND ("VehicleArrivedAtUtc" IS NULL OR ("ChargingBayId" IS NOT NULL AND "CheckedInAtUtc" IS NOT NULL))
                AND ("VehicleDepartedAtUtc" IS NULL OR ("VehicleArrivedAtUtc" IS NOT NULL AND "Status" = 'Completed'))
                AND ("NoChargeConfirmedAtUtc" IS NULL OR "VehicleArrivedAtUtc" IS NOT NULL)
                AND CASE "Status"
                    WHEN 'Held' THEN "CheckedInAtUtc" IS NULL AND "ClosedAtUtc" IS NULL AND "ClosedById" IS NULL
                    WHEN 'CheckedIn' THEN "CheckedInAtUtc" IS NOT NULL AND "ClosedAtUtc" IS NULL AND "ClosedById" IS NULL
                    WHEN 'Completed' THEN "CheckedInAtUtc" IS NOT NULL AND "ClosedAtUtc" IS NOT NULL AND "ClosedById" IS NOT NULL
                    WHEN 'Cancelled' THEN "ClosedAtUtc" IS NOT NULL AND "ClosedById" IS NOT NULL
                    WHEN 'Expired' THEN "CheckedInAtUtc" IS NULL AND "ClosedAtUtc" IS NOT NULL AND "ClosedById" IS NULL
                    ELSE FALSE
                END
                """);
        });
        builder.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CheckedInById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.VehicleArrivedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.VehicleDepartedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.NoChargeConfirmedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChargingBay>().WithMany().HasForeignKey(x => x.ChargingBayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Battery>().WithMany().HasForeignKey(x => x.BatteryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ClosedById).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.ClosingReason).HasMaxLength(40);
        builder.Property(x => x.Code).HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => new { x.CustomerId, x.IdempotencyKey }, "ux_reservations_customer_request")
            .IsUnique().HasFilter(""" "IdempotencyKey" IS NOT NULL """);
        builder.HasIndex(x => new { x.Status, x.ExpiresAtUtc });
        const string active = """ "Status" IN ('Held', 'CheckedIn') """;
        builder.HasIndex(x => x.CustomerId, CustomerHoldIndex).IsUnique().HasFilter(active);
        builder.HasIndex(x => x.ChargingBayId, BayHoldIndex).IsUnique()
            .HasFilter(active + """ AND "ChargingBayId" IS NOT NULL""");
        builder.HasIndex(x => x.BatteryId, BatteryHoldIndex).IsUnique()
            .HasFilter(active + """ AND "BatteryId" IS NOT NULL""");
        builder.HasIndex(x => x.LocationId);
        builder.HasIndex(x => x.LocationId, "ix_reservations_active_location").HasFilter(active);
        builder.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc });
        builder.HasIndex(x => x.ChargingBayId);
        builder.HasIndex(x => x.BatteryId);
    }

    public void Configure(EntityTypeBuilder<ChargingBay> builder)
    {
        builder.ToTable("charging_bays", table => table.HasCheckConstraint("ck_charging_bays_power", """ "PowerKw" > 0 """));
        builder.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StateUpdatedById).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Code).HasMaxLength(64);
        builder.HasIndex(x => new { x.LocationId, x.Code }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Battery> builder)
    {
        builder.ToTable("batteries");
        builder.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StateUpdatedById).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Serial).HasMaxLength(64);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CustodianCustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.Serial).IsUnique();
    }

    public void Configure(EntityTypeBuilder<ServiceHours> builder)
    {
        builder.ToTable("service_hours", table => table.HasCheckConstraint("ck_service_hours_interval",
            """ "OpensAtMinute" >= 0 AND "OpensAtMinute" < "ClosesAtMinute" AND "ClosesAtMinute" <= 1440 """));
        builder.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UpdatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LocationId, x.ServiceType, x.Day, x.OpensAtMinute, x.ClosesAtMinute }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<OperatorAssignment> builder)
    {
        builder.ToTable("operator_assignments");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.OperatorId, "ux_operator_active_assignment").IsUnique()
            .HasFilter("\"EndedAtUtc\" IS NULL");
        builder.HasIndex(x => new { x.OperatorId, x.AssignedAtUtc });
        builder.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssignedByAdminId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ChargingSessionConfiguration : IEntityTypeConfiguration<ChargingSession>
{
    public void Configure(EntityTypeBuilder<ChargingSession> builder)
    {
        builder.ToTable("charging_sessions", table =>
        {
            table.HasCheckConstraint("ck_charging_sessions_start_meter", "\"StartMeterKwh\" >= 0");
            table.HasCheckConstraint("ck_charging_sessions_end_meter",
                "\"EndMeterKwh\" IS NULL OR \"EndMeterKwh\" >= \"StartMeterKwh\"");
            table.HasCheckConstraint("ck_charging_sessions_completion",
                "(\"CompletedAtUtc\" IS NULL AND \"EndMeterKwh\" IS NULL AND \"CompletedById\" IS NULL) OR " +
                "(\"CompletedAtUtc\" IS NOT NULL AND \"EndMeterKwh\" IS NOT NULL AND \"CompletedById\" IS NOT NULL)");
        });
        builder.HasOne<Reservation>().WithOne().HasForeignKey<ChargingSession>(x => x.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChargingBay>().WithMany().HasForeignKey(x => x.ChargingBayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StartedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CompletedById).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.StartMeterKwh).HasPrecision(12, 3);
        builder.Property(x => x.AppliedKwhRate).HasPrecision(12, 2);
        builder.Property(x => x.EndMeterKwh).HasPrecision(12, 3);
        builder.HasIndex(x => x.ReservationId).IsUnique();
    }
}
