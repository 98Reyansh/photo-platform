using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Photos;

namespace PhotoPlatform.Infrastructure.Persistence.Configurations;

public sealed class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        builder.ToTable("Photos");

        builder.HasKey(photo => photo.Id);

        builder.Property(photo => photo.Id)
            .ValueGeneratedNever();

        builder.Property(photo => photo.TenantId)
            .IsRequired();

        builder.Property(photo => photo.Title)
            .IsRequired()
            .HasMaxLength(Photo.TitleMaxLength);

        builder.Property(photo => photo.Description)
            .HasMaxLength(Photo.DescriptionMaxLength);

        builder.Property(photo => photo.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(photo => photo.UpdatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(photo => photo.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(photo => photo.TenantId);
    }
}
