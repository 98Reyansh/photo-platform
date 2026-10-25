using PhotoPlatform.Api.Infrastructure;
using PhotoPlatform.Infrastructure;
using Swashbuckle.AspNetCore;

if (StudioBootstrapCommand.IsRequested(args))
{
    Environment.ExitCode = await StudioBootstrapEntry.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(PhotoOriginalUploadLimits.ConfigureKestrel);
builder.Services.AddSingleton<PhotoUploadSpoolGate>();
builder.Services.AddSingleton<PhotoOriginalMultipartReader>();
builder.Services.AddControllers(options =>
{
    options.Filters.Add(new CsrfValidationFilter());
});
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddStudioAuthentication(builder.Environment);

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "PhotoPlatform API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseCors(FrontendCorsExtensions.PolicyName);
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<StudioResolutionMiddleware>();
app.MapControllers();

app.Run();

public partial class Program;
