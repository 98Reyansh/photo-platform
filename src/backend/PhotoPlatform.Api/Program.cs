using PhotoPlatform.Api.Infrastructure;
using Swashbuckle.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddFrontendCors(builder.Configuration);

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
app.MapControllers();

app.Run();
