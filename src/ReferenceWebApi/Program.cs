using ReferenceWebApi.Configuration;
using Serilog;

// ──────────────────────────────────────────────────────────────────────────────
// Bootstrap logger — available immediately, before the host is built.
// This lets us log configuration source loading, connection failures, and
// other startup diagnostics that would otherwise be invisible.
// ──────────────────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] (bootstrap) {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Reference Web API");
    Log.Information("Environment: {Environment}",
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production");

    var builder = WebApplication.CreateBuilder(args);

    // ──────────────────────────────────────────────────────────────────────
    // CONFIGURATION — build sources in precedence order with logging
    // ──────────────────────────────────────────────────────────────────────
    builder.Configuration.AddConfigurationSources(args, builder.Environment.EnvironmentName);

    // ──────────────────────────────────────────────────────────────────────
    // SERILOG — replace the bootstrap logger with the fully-configured one.
    // From this point, Serilog reads its settings from configuration (which
    // now includes all sources: appsettings, Azure App Config, env vars…).
    // ──────────────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((context, services, loggerConfig) =>
    {
        loggerConfig.ReadFrom.Configuration(context.Configuration);
        Log.Information("Serilog reconfigured from full configuration pipeline");
    });

    // ──────────────────────────────────────────────────────────────────────
    // BIND OPTIONS — strongly-typed configuration with validation
    // ──────────────────────────────────────────────────────────────────────
    builder.Services
        .AddOptions<ApiOptions>()
        .BindConfiguration(ApiOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services
        .AddOptions<AzureAppConfigurationOptions>()
        .BindConfiguration(AzureAppConfigurationOptions.SectionName);

    builder.Services
        .AddOptions<AzureKeyVaultOptions>()
        .BindConfiguration(AzureKeyVaultOptions.SectionName);

    // ──────────────────────────────────────────────────────────────────────
    // AZURE APP CONFIGURATION MIDDLEWARE (refresh on requests)
    // ──────────────────────────────────────────────────────────────────────
    var appConfigOptions = new AzureAppConfigurationOptions();
    builder.Configuration.GetSection(AzureAppConfigurationOptions.SectionName).Bind(appConfigOptions);

    if (appConfigOptions.IsConfigured)
    {
        builder.Services.AddAzureAppConfiguration();
    }

    // ──────────────────────────────────────────────────────────────────────
    // SERVICES
    // ──────────────────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddHealthChecks();

    // ──────────────────────────────────────────────────────────────────────
    // BUILD
    // ──────────────────────────────────────────────────────────────────────
    var app = builder.Build();

    LogConfigurationSummary(app.Configuration, app.Environment);

    // ──────────────────────────────────────────────────────────────────────
    // MIDDLEWARE PIPELINE
    // ──────────────────────────────────────────────────────────────────────
    app.UseSerilogRequestLogging();

    if (appConfigOptions.IsConfigured)
    {
        app.UseAzureAppConfiguration();
    }

    var apiOptions = new ApiOptions();
    app.Configuration.GetSection(ApiOptions.SectionName).Bind(apiOptions);

    if (apiOptions.EnableSwagger)
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/healthz");

    Log.Information("Reference Web API is ready — listening for requests");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// ──────────────────────────────────────────────────────────────────────────────
// Helpers
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Logs a summary of all configuration providers that were loaded,
/// in the order they were added. This makes it easy to verify precedence
/// and diagnose "where did this value come from?" problems.
/// </summary>
static void LogConfigurationSummary(IConfiguration configuration, IWebHostEnvironment environment)
{
    Log.Information("──── Configuration Summary ────");
    Log.Information("Environment: {Environment}", environment.EnvironmentName);
    Log.Information("Content root: {ContentRoot}", environment.ContentRootPath);

    if (configuration is IConfigurationRoot configRoot)
    {
        var providers = configRoot.Providers.ToList();
        Log.Information("Loaded {Count} configuration providers (last wins):", providers.Count);

        for (var i = 0; i < providers.Count; i++)
        {
            var provider = providers[i];
            var providerType = provider.GetType().Name;
            Log.Information("  [{Index}] {Provider}", i + 1, providerType);
        }
    }

    Log.Information("───────────────────────────────");
}
