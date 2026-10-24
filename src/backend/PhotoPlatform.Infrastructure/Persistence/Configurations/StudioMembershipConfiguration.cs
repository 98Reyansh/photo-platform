using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Membership;

namespace PhotoPlatform.Infrastructure.Persistence.Configurations;

public sealed class StudioMembershipConfiguration : IEntityTypeConfiguration<StudioMembership>
{
    public void Configure(EntityTypeBuilder<StudioMembership> builder)
    {
        builder.ToTable("StudioMemberships");

        builder.HasKey(membership => membership.Id);

        builder.Property(membership => membership.Id)
            .ValueGeneratedNever();

        builder.Property(membership => membership.UserId)
            .IsRequired();

        builder.HasIndex(membership => membership.UserId)
            .IsUnique();

        builder.Property(membership => membership.TenantId)
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(membership => membership.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(membership => membership.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}
