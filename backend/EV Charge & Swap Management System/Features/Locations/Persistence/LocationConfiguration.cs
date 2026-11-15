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
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasIndex(x => x.Name);
    }
}
