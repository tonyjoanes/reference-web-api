using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Platform.Hosting.Internal;
using Platform.Hosting.Options;
using Serilog;
using Serilog.Events;

namespace Platform.Hosting;

/// <summary>
/// Opinionated extension methods that configure the middleware pipeline
/// in the organisation's standard order. This ordering is fixed and
/// cannot be changed by consuming services.
///
/// <para>Pipeline order (each numbered step is non-negotiable):</para>
///
/// <list type="number">
///   <item><description>Exception handler (catches everything below)</description></item>
///   <item><description>Security headers (on every response, including errors)</description></item>
///   <item><description>HSTS + HTTPS redirection (redirect before work)</description></item>
///   <item><description>Correlation ID (available to all downstream middleware)</description></item>
///   <item><description>Serilog request logging (enriched with correlation ID)</description></item>
///   <item><description>Azure App Configuration refresh (per-request)</description></item>
/// </list>
///
/// <para>
/// After calling <see cref="UseStandardPipeline"/>, the consuming service
/// adds its own middleware (Swagger, auth, routing, endpoints, etc.).
/// </para>
/// </summary>
public static class StandardPipelineExtensions
{
    /// <summary>
    /// Adds the standard middleware pipeline. Call this before mapping
    /// your own endpoints or adding app-specific middleware.
    /// </summary>
    /// <returns>The same app, for chaining.</returns>
    public static WebApplication UseStandardPipeline(this WebApplication app)
    {
        LogConfigurationSummary(app.Configuration, app.Environment);

        // 1. Global exception handler — returns RFC 7807 Problem Details.
        //    Must be first so it wraps everything below.
        app.UseExceptionHandler(exceptionApp =>
        {
            exceptionApp.Run(async context =>
            {
                context.Response.ContentType = "application/problem+json";

                var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
                var correlationId = context.Items[CorrelationIdMiddleware.ItemKey]?.ToString();

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

        // 3. HSTS + HTTPS
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
                    httpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString() ?? "unknown");
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
        var appConfigOptions = new AzureAppConfigurationOptions();
        app.Configuration
            .GetSection(AzureAppConfigurationOptions.SectionName)
            .Bind(appConfigOptions);

        if (appConfigOptions.IsConfigured)
        {
            app.UseAzureAppConfiguration();
        }

        return app;
    }

    /// <summary>
    /// Logs a summary of all configuration providers that were loaded,
    /// in the order they were added. Makes it easy to verify precedence
    /// and diagnose "where did this value come from?" problems.
    /// </summary>
    private static void LogConfigurationSummary(
        IConfiguration configuration,
        IWebHostEnvironment environment)
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
                Log.Information("  [{Index}] {Provider}", i + 1, providers[i].GetType().Name);
            }
        }

        Log.Information("───────────────────────────────");
    }
}
