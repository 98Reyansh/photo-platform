using PhotoPlatform.Domain.Entities;

namespace PhotoPlatform.Domain.Tests;

public class TenantTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 20, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Constructor_trims_name_and_normalizes_slug()
    {
        var tenant = new Tenant("  North Studio  ", "  North-Studio  ", UtcNow);

        Assert.Equal("North Studio", tenant.Name);
        Assert.Equal("north-studio", tenant.Slug);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(7, tenant.Id.Version);
        Assert.NotEqual(Guid.Empty, tenant.Id);
        Assert.Equal(DateTimeKind.Utc, tenant.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, tenant.UpdatedAtUtc.Kind);
        Assert.Equal(UtcNow, tenant.CreatedAtUtc);
        Assert.Equal(UtcNow, tenant.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string? name)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => new Tenant(name!, "studio", UtcNow));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Slug_is_required(string? slug)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => new Tenant("Studio", slug!, UtcNow));

        Assert.Equal("slug", exception.ParamName);
    }

    [Fact]
    public void Name_cannot_exceed_its_maximum_length()
    {
        var name = new string('a', Tenant.NameMaxLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => new Tenant(name, "studio", UtcNow));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Slug_cannot_exceed_its_maximum_length()
    {
        var slug = new string('a', Tenant.SlugMaxLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => new Tenant("Studio", slug, UtcNow));

        Assert.Equal("slug", exception.ParamName);
    }

    [Theory]
    [InlineData("North Studio")]
    [InlineData("-studio")]
    [InlineData("studio-")]
    [InlineData("north--studio")]
    [InlineData("north_studio")]
    public void Slug_rejects_values_that_are_not_normalized_slugs(string slug)
    {
        Assert.Throws<ArgumentException>(() => new Tenant("Studio", slug, UtcNow));
    }

    [Fact]
    public void Different_slug_spellings_normalize_to_the_same_value()
    {
        var lower = new Tenant("Studio", "north-studio", UtcNow);
        var mixed = new Tenant("Studio", "  North-Studio  ", UtcNow);

        Assert.Equal(lower.Slug, mixed.Slug);
    }

    [Fact]
    public void Timestamp_must_be_utc()
    {
        var local = DateTime.SpecifyKind(UtcNow, DateTimeKind.Local);
        var unspecified = DateTime.SpecifyKind(UtcNow, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() => new Tenant("Studio", "studio", local));
        Assert.Throws<ArgumentException>(() => new Tenant("Studio", "studio", unspecified));
    }
}
