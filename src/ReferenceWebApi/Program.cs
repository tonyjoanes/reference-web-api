using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ReferenceWebApi.Configuration;
using ReferenceWebApi.Middleware;
using Serilog;
using Serilog.Events;

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
    // BIND OPTIONS — strongly-typed configuration with validation.
    // ValidateOnStart() means the app fails fast on misconfiguration
    // rather than discovering it on the first request.
    // ──────────────────────────────────────────────────────────────────────
    builder.Services
        .AddOptions<ApiOptions>()
        .BindConfiguration(ApiOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services
        .AddOptions<AzureAppConfigurationOptions>()
        .BindConfiguration(AzureAppConfigurationOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services
        .AddOptions<AzureKeyVaultOptions>()
        .BindConfiguration(AzureKeyVaultOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // ──────────────────────────────────────────────────────────────────────
    // AZURE APP CONFIGURATION MIDDLEWARE (refresh on requests).
    // Read once to decide whether to register the middleware service.
    // ──────────────────────────────────────────────────────────────────────
    var appConfigOptions = new AzureAppConfigurationOptions();
    builder.Configuration.GetSection(AzureAppConfigurationOptions.SectionName).Bind(appConfigOptions);
    var useAzureAppConfig = appConfigOptions.IsConfigured;

    if (useAzureAppConfig)
    {
        builder.Services.AddAzureAppConfiguration();
    }

    // ──────────────────────────────────────────────────────────────────────
    // PROBLEM DETAILS — RFC 7807 standardised error responses
    // ──────────────────────────────────────────────────────────────────────
    builder.Services.AddProblemDetails(options =>
    {
        options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["correlationId"] =
                context.HttpContext.Items["CorrelationId"]?.ToString();
        };
    });

    // ──────────────────────────────────────────────────────────────────────
    // SERVICES
    // ──────────────────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // ──────────────────────────────────────────────────────────────────────
    // HEALTH CHECKS — verify Azure dependencies are reachable
    // ──────────────────────────────────────────────────────────────────────
    var healthChecks = builder.Services.AddHealthChecks();

    var kvOptions = new AzureKeyVaultOptions();
    builder.Configuration.GetSection(AzureKeyVaultOptions.SectionName).Bind(kvOptions);
    if (kvOptions.IsConfigured)
    {
        healthChecks.AddAzureKeyVault(
            new Uri(kvOptions.VaultUri),
            new Azure.Identity.DefaultAzureCredential(),
            options => { options.AddSecret("health-check-probe"); },
            name: "azure-key-vault",
            tags: ["azure", "secrets"]);
        Log.Information("Health check registered: Azure Key Vault ({VaultUri})", kvOptions.VaultUri);
    }

    // ──────────────────────────────────────────────────────────────────────
    // BUILD
    // ──────────────────────────────────────────────────────────────────────
    var app = builder.Build();

    LogConfigurationSummary(app.Configuration, app.Environment);

    // ──────────────────────────────────────────────────────────────────────
    // MIDDLEWARE PIPELINE — order matters!
    //
    //  1. Exception handler   (catches everything below it)
    //  2. Security headers    (applied to every response, including errors)
    //  3. HSTS / HTTPS        (redirect before any real work)
    //  4. Correlation ID      (available to all downstream middleware)
    //  5. Serilog request log (enriched with correlation ID)
    //  6. Azure App Config    (refresh configuration on each request)
    //  7. Swagger             (dev only, before routing)
    //  8. Auth / Routing / Endpoints
    // ──────────────────────────────────────────────────────────────────────

    // 1. Global exception handler — returns RFC 7807 Problem Details
    app.UseExceptionHandler(exceptionApp =>
    {
        exceptionApp.Run(async context =>
        {
            context.Response.ContentType = "application/problem+json";

            var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
            var correlationId = context.Items["CorrelationId"]?.ToString();

            Log.Error(exceptionFeature?.Error,
                "Unhandled exception for {Method} {Path} (CorrelationId: {CorrelationId})",
                context.Request.Method, context.Request.Path, correlationId);

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred",
                Detail = app.Environment.IsDevelopment()
                    ? exceptionFeature?.Error?.Message
                    : "An internal error occurred. Use the correlation ID to find details in application logs.",
                Instance = context.Request.Path,
            };
            problem.Extensions["correlationId"] = correlationId;

            context.Response.StatusCode = problem.Status.Value;
            await context.Response.WriteAsJsonAsync(problem);
        });
    });

    // 2. Security headers
    app.UseMiddleware<SecurityHeadersMiddleware>();

    // 3. HTTPS
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }
    app.UseHttpsRedirection();

    // 4. Correlation ID
    app.UseMiddleware<CorrelationIdMiddleware>();

    // 5. Serilog request logging with enrichment
    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("CorrelationId",
                httpContext.Items["CorrelationId"]?.ToString() ?? "unknown");
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("UserAgent",
                httpContext.Request.Headers.UserAgent.ToString());
        };
        options.GetLevel = (httpContext, elapsed, ex) =>
        {
            if (ex is not null || httpContext.Response.StatusCode >= 500)
                return LogEventLevel.Error;
            if (httpContext.Response.StatusCode >= 400)
                return LogEventLevel.Warning;
            return LogEventLevel.Information;
        };
    });

    // 6. Azure App Configuration refresh
    if (useAzureAppConfig)
    {
        app.UseAzureAppConfiguration();
    }

    // 7. Swagger
    var apiOptions = new ApiOptions();
    app.Configuration.GetSection(ApiOptions.SectionName).Bind(apiOptions);
    if (apiOptions.EnableSwagger)
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // 8. Routing / Authorization / Endpoints
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/healthz");

    Log.Information("Reference Web API is ready — listening for requests");
    app.Run();
}
catch (OptionsValidationException ex)
{
    // Surface configuration validation failures clearly at startup
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
