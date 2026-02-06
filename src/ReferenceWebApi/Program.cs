using Microsoft.Extensions.Options;
using Platform.Hosting;
using ReferenceWebApi.Configuration;
using Serilog;

// ──────────────────────────────────────────────────────────────────────────────
// Standard bootstrap logger — captures startup diagnostics to the console
// before the host is built. Provided by Platform.Hosting.
// ──────────────────────────────────────────────────────────────────────────────
BootstrapLogger.Create();

try
{
    Log.Information("Starting Reference Web API");

    // ──────────────────────────────────────────────────────────────────────
    // Platform standard: configuration sources (in fixed precedence order),
    // Serilog, options validation, health checks, Problem Details.
    // ──────────────────────────────────────────────────────────────────────
    var builder = WebApplication.CreateBuilder(args)
        .AddStandardConfiguration(args);

    // ──────────────────────────────────────────────────────────────────────
    // App-specific options
    // ──────────────────────────────────────────────────────────────────────
    builder.Services
        .AddOptions<ApiOptions>()
        .BindConfiguration(ApiOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // ──────────────────────────────────────────────────────────────────────
    // App-specific services
    // ──────────────────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    // ──────────────────────────────────────────────────────────────────────
    // Platform standard: middleware pipeline in fixed order
    // (exception handler → security headers → HTTPS → correlation ID →
    //  Serilog → Azure App Config refresh)
    // ──────────────────────────────────────────────────────────────────────
    app.UseStandardPipeline();

    // ──────────────────────────────────────────────────────────────────────
    // App-specific middleware and endpoints
    // ──────────────────────────────────────────────────────────────────────
    var apiOptions = new ApiOptions();
    app.Configuration.GetSection(ApiOptions.SectionName).Bind(apiOptions);
    if (apiOptions.EnableSwagger)
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseAuthorization();
    app.MapControllers();

    Log.Information("Reference Web API is ready — listening for requests");
    app.Run();
}
catch (OptionsValidationException ex)
{
    Log.Fatal("Configuration validation failed on startup: {Failures}",
        string.Join("; ", ex.Failures));
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
