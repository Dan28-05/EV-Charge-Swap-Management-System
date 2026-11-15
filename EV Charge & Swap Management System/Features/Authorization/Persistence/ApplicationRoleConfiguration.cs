using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EVChargeSwap.Features.Authorization.Persistence;

public sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.ToTable("roles");
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        builder.Property(x => x.NormalizedName).HasColumnName("normalized_name").HasMaxLength(256).IsRequired();
        builder.Property(x => x.ConcurrencyStamp).HasColumnName("concurrency_stamp").IsConcurrencyToken();
        builder.HasIndex(x => x.NormalizedName)
            .HasDatabaseName("ux_roles_normalized_name").IsUnique();

        builder.HasData(
            new IdentityRole<Guid>
            {
                Id = AppRoles.CustomerId,
                Name = AppRoles.Customer,
                NormalizedName = AppRoles.Customer.ToUpperInvariant(),
                ConcurrencyStamp = "a67df661-4fdd-4671-8f09-8f556fca8135"
            },
            new IdentityRole<Guid>
            {
                Id = AppRoles.OperatorId,
                Name = AppRoles.Operator,
                NormalizedName = AppRoles.Operator.ToUpperInvariant(),
                ConcurrencyStamp = "10b325e4-bf98-46ae-9e3a-28409939064e"
            },
            new IdentityRole<Guid>
            {
                Id = AppRoles.AdminId,
                Name = AppRoles.Admin,
                NormalizedName = AppRoles.Admin.ToUpperInvariant(),
                ConcurrencyStamp = "828b9e50-f9ea-4221-b7d8-d6bb78d3ff44"
            });
    }
}
