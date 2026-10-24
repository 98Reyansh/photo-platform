using PhotoPlatform.Domain.Photos;

namespace PhotoPlatform.Domain.Tests;

public class PhotoTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid TenantId = Guid.CreateVersion7();

    [Fact]
    public void Constructor_keeps_the_studio_and_trims_text()
    {
        var photo = new Photo(TenantId, "  Harbor light  ", "  Before sunrise  ", CreatedAtUtc);

        Assert.Equal(TenantId, photo.TenantId);
        Assert.Equal("Harbor light", photo.Title);
        Assert.Equal("Before sunrise", photo.Description);
        Assert.NotEqual(Guid.Empty, photo.Id);
        Assert.Equal(7, photo.Id.Version);
        Assert.Equal(CreatedAtUtc, photo.CreatedAtUtc);
        Assert.Equal(CreatedAtUtc, photo.UpdatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, photo.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, photo.UpdatedAtUtc.Kind);
    }

    [Fact]
    public void Empty_tenant_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new Photo(Guid.Empty, "Harbor", null, CreatedAtUtc));

        Assert.Equal("tenantId", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Title_is_required(string? title)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new Photo(TenantId, title!, null, CreatedAtUtc));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void Title_cannot_exceed_its_maximum_length()
    {
        var title = new string('a', Photo.TitleMaxLength + 1);

        var exception = Assert.Throws<ArgumentException>(() =>
            new Photo(TenantId, title, null, CreatedAtUtc));

        Assert.Equal("title", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_description_is_stored_as_null(string? description)
    {
        var photo = new Photo(TenantId, "Harbor", description, CreatedAtUtc);

        Assert.Null(photo.Description);
    }

    [Fact]
    public void Description_cannot_exceed_its_maximum_length()
    {
        var description = new string('a', Photo.DescriptionMaxLength + 1);

        var exception = Assert.Throws<ArgumentException>(() =>
            new Photo(TenantId, "Harbor", description, CreatedAtUtc));

        Assert.Equal("description", exception.ParamName);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Non_utc_timestamp_is_rejected(DateTimeKind kind)
    {
        var timestamp = DateTime.SpecifyKind(CreatedAtUtc, kind);

        var exception = Assert.Throws<ArgumentException>(() =>
            new Photo(TenantId, "Harbor", null, timestamp));

        Assert.Equal("createdAtUtc", exception.ParamName);
    }

    [Fact]
    public void Update_changes_text_and_the_updated_timestamp_only()
    {
        var photo = new Photo(TenantId, "Harbor", "Dawn", CreatedAtUtc);
        var id = photo.Id;
        var updatedAtUtc = CreatedAtUtc.AddHours(2);

        photo.UpdateDetails("  Evening harbor  ", "  After sunset  ", updatedAtUtc);

        Assert.Equal(id, photo.Id);
        Assert.Equal(TenantId, photo.TenantId);
        Assert.Equal(CreatedAtUtc, photo.CreatedAtUtc);
        Assert.Equal("Evening harbor", photo.Title);
        Assert.Equal("After sunset", photo.Description);
        Assert.Equal(updatedAtUtc, photo.UpdatedAtUtc);
    }

    [Fact]
    public void Update_can_clear_the_description()
    {
        var photo = new Photo(TenantId, "Harbor", "Dawn", CreatedAtUtc);
        var updatedAtUtc = CreatedAtUtc.AddMinutes(30);

        photo.UpdateDetails("Harbor", "   ", updatedAtUtc);

        Assert.Null(photo.Description);
        Assert.Equal("Harbor", photo.Title);
        Assert.Equal(updatedAtUtc, photo.UpdatedAtUtc);
    }

    [Fact]
    public void Invalid_update_does_not_change_the_photo()
    {
        var photo = new Photo(TenantId, "Harbor", "Dawn", CreatedAtUtc);
        var id = photo.Id;
        var tooLong = new string('a', Photo.TitleMaxLength + 1);

        var exception = Assert.Throws<ArgumentException>(() =>
            photo.UpdateDetails(tooLong, "Replacement", CreatedAtUtc.AddHours(1)));

        Assert.Equal("title", exception.ParamName);
        Assert.Equal(id, photo.Id);
        Assert.Equal(TenantId, photo.TenantId);
        Assert.Equal("Harbor", photo.Title);
        Assert.Equal("Dawn", photo.Description);
        Assert.Equal(CreatedAtUtc, photo.CreatedAtUtc);
        Assert.Equal(CreatedAtUtc, photo.UpdatedAtUtc);
    }

    [Fact]
    public void Non_utc_update_does_not_change_the_photo()
    {
        var photo = new Photo(TenantId, "Harbor", "Dawn", CreatedAtUtc);
        var local = DateTime.SpecifyKind(CreatedAtUtc.AddHours(1), DateTimeKind.Local);

        var exception = Assert.Throws<ArgumentException>(() =>
            photo.UpdateDetails("Evening", "Night", local));

        Assert.Equal("updatedAtUtc", exception.ParamName);
        Assert.Equal("Harbor", photo.Title);
        Assert.Equal("Dawn", photo.Description);
        Assert.Equal(CreatedAtUtc, photo.UpdatedAtUtc);
    }

    [Fact]
    public void Overlong_description_update_does_not_change_the_photo()
    {
        var photo = new Photo(TenantId, "Harbor", "Dawn", CreatedAtUtc);
        var id = photo.Id;
        var tooLong = new string('a', Photo.DescriptionMaxLength + 1);
        var updatedAtUtc = CreatedAtUtc.AddHours(1);

        var exception = Assert.Throws<ArgumentException>(() =>
            photo.UpdateDetails("Evening harbor", tooLong, updatedAtUtc));

        Assert.Equal("description", exception.ParamName);
        Assert.Equal(id, photo.Id);
        Assert.Equal(TenantId, photo.TenantId);
        Assert.Equal("Harbor", photo.Title);
        Assert.Equal("Dawn", photo.Description);
        Assert.Equal(CreatedAtUtc, photo.CreatedAtUtc);
        Assert.Equal(CreatedAtUtc, photo.UpdatedAtUtc);
    }
}
