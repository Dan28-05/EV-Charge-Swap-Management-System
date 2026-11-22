using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EVChargeSwap.Features.Locations.Entities;

namespace EVChargeSwap.Features.Locations.Persistence;

public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations", table =>
        {
            table.HasCheckConstraint("ck_locations_latitude", "latitude BETWEEN -90 AND 90");
            table.HasCheckConstraint("ck_locations_longitude", "longitude BETWEEN -180 AND 180");
            table.HasCheckConstraint("ck_locations_service", "offers_car_charging OR offers_battery_swap");
            table.HasCheckConstraint("ck_locations_operating_status", "operating_status IN ('Unknown', 'Open', 'Closed', 'Maintenance')");
            table.HasCheckConstraint("ck_locations_charging_power", "charging_power_kw IS NULL OR (offers_car_charging AND charging_power_kw IN (30, 60, 150, 250))");
            table.HasCheckConstraint("ck_locations_battery_capacity", "battery_capacity_kwh IS NULL OR (offers_battery_swap AND battery_capacity_kwh = 1.5)");
            table.HasCheckConstraint("ck_locations_available_batteries", "available_batteries IS NULL OR (offers_battery_swap AND available_batteries BETWEEN 0 AND 20)");
            table.HasCheckConstraint("ck_locations_available_charging_bays", "available_charging_bays IS NULL OR (offers_car_charging AND available_charging_bays BETWEEN 0 AND 10)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Address).HasColumnName("address").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Latitude).HasColumnName("latitude").HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasColumnName("longitude").HasPrecision(9, 6);
        builder.Property(x => x.OffersCarCharging).HasColumnName("offers_car_charging");
        builder.Property(x => x.OffersBatterySwap).HasColumnName("offers_battery_swap");
        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.IsOpen24Hours).HasColumnName("is_open_24_hours");
        builder.Property(x => x.ChargingPowerKw).HasColumnName("charging_power_kw");
        builder.Property(x => x.BatteryCapacityKwh).HasColumnName("battery_capacity_kwh").HasPrecision(3, 1);
        builder.Property(x => x.AvailableBatteries).HasColumnName("available_batteries");
        builder.Property(x => x.AvailableChargingBays).HasColumnName("available_charging_bays");
        builder.Property(x => x.OperatingStatus).HasColumnName("operating_status")
            .HasConversion<string>().HasMaxLength(20).HasDefaultValue(Entities.LocationOperatingStatus.Unknown);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => new { x.IsActive, x.Latitude, x.Longitude });
        builder.HasIndex(x => new { x.IsActive, x.OperatingStatus, x.Name, x.Id });
    }
}
