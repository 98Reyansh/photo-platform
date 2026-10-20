using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public const string StatusCheckConstraintName = "CK_Tenants_Status";

    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants", table =>
        {
            table.HasCheckConstraint(
                StatusCheckConstraintName,
                "\"Status\" IN ('Active', 'Suspended')");
        });

        builder.HasKey(tenant => tenant.Id);

        builder.Property(tenant => tenant.Id)
            .ValueGeneratedNever();

        builder.Property(tenant => tenant.Name)
            .IsRequired()
            .HasMaxLength(Tenant.NameMaxLength);

        builder.Property(tenant => tenant.Slug)
            .IsRequired()
            .HasMaxLength(Tenant.SlugMaxLength);

        builder.HasIndex(tenant => tenant.Slug)
            .IsUnique();

        builder.Property(tenant => tenant.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(tenant => tenant.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(tenant => tenant.UpdatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}
