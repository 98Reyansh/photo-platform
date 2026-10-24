using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Tests;

public class PhotoQueryFilterTests
{
    [Fact]
    public void Unresolved_studio_matches_no_photo_tenant()
    {
        using var context = CreateContext(new CurrentStudio());

        var sql = context.Photos.ToQueryString();

        Assert.Contains("FALSE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Guid.Empty.ToString(), sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolved_studio_is_a_query_parameter_for_that_context()
    {
        var firstTenant = Guid.CreateVersion7();
        var secondTenant = Guid.CreateVersion7();
        var firstStudio = new CurrentStudio();
        var secondStudio = new CurrentStudio();
        firstStudio.UseTenant(firstTenant);
        secondStudio.UseTenant(secondTenant);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ModelOnlyConnection)
            .Options;

        using var first = new ApplicationDbContext(options, firstStudio);
        using var second = new ApplicationDbContext(options, secondStudio);

        Assert.Same(first.Model, second.Model);

        var firstSql = first.Photos.ToQueryString();
        var secondSql = second.Photos.ToQueryString();

        Assert.Contains("TRUE", firstSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(firstTenant.ToString(), firstSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secondTenant.ToString(), firstSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(secondTenant.ToString(), secondSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(firstTenant.ToString(), secondSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Studio_resolved_after_context_creation_changes_only_that_context_filter()
    {
        var studio = new CurrentStudio();
        using var context = CreateContext(studio);
        var before = context.Photos.ToQueryString();

        var tenantId = Guid.CreateVersion7();
        studio.UseTenant(tenantId);
        var after = context.Photos.ToQueryString();

        Assert.Contains("FALSE", before, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(tenantId.ToString(), before, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TRUE", after, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(tenantId.ToString(), after, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scoped_studios_do_not_share_a_tenant()
    {
        var services = new ServiceCollection();
        services.AddScoped<CurrentStudio>();
        services.AddScoped<ICurrentStudio>(provider => provider.GetRequiredService<CurrentStudio>());
        services.AddScoped<ICurrentStudioSetter>(provider => provider.GetRequiredService<CurrentStudio>());
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(ModelOnlyConnection));

        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var firstTenant = Guid.CreateVersion7();
        var secondTenant = Guid.CreateVersion7();
        firstScope.ServiceProvider.GetRequiredService<ICurrentStudioSetter>().UseTenant(firstTenant);
        secondScope.ServiceProvider.GetRequiredService<ICurrentStudioSetter>().UseTenant(secondTenant);

        var first = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Same(first.Model, second.Model);
        Assert.Contains(firstTenant.ToString(), first.Photos.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(secondTenant.ToString(), second.Photos.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        Assert.NotSame(
            firstScope.ServiceProvider.GetRequiredService<ICurrentStudio>(),
            secondScope.ServiceProvider.GetRequiredService<ICurrentStudio>());
    }

    [Fact]
    public void Empty_or_repeated_tenant_resolution_is_rejected()
    {
        var studio = new CurrentStudio();

        var exception = Assert.Throws<ArgumentException>(() => studio.UseTenant(Guid.Empty));
        Assert.Equal("tenantId", exception.ParamName);
        Assert.False(studio.IsResolved);

        studio.UseTenant(Guid.CreateVersion7());

        Assert.Throws<InvalidOperationException>(() => studio.UseTenant(Guid.CreateVersion7()));
    }

    private const string ModelOnlyConnection =
        "Host=127.0.0.1;Port=1;Database=model_only;Username=photoplatform;Password=unused;Timeout=1";

    private static ApplicationDbContext CreateContext(ICurrentStudio studio)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ModelOnlyConnection)
            .Options;

        return new ApplicationDbContext(options, studio);
    }
}
