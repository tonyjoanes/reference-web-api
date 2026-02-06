using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Platform.Hosting.Exceptions;
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
/// Health check endpoints are also mapped automatically:
/// <c>/healthz/live</c> (liveness) and <c>/healthz/ready</c> (readiness).
/// </para>
/// </summary>
public static class StandardPipelineExtensions
{
    /// <summary>
    /// Adds the standard middleware pipeline and maps health check endpoints.
    /// Call this before mapping your own endpoints or adding app-specific middleware.
    /// </summary>
    /// <returns>The same app, for chaining.</returns>
    public static WebApplication UseStandardPipeline(this WebApplication app)
    {
        LogConfigurationSummary(app.Configuration, app.Environment);

        // 1. Global exception handler — returns RFC 7807 Problem Details.
        //    Must be first so it wraps everything below.
        //    PlatformException subclasses map to their declared HTTP status code.
        app.UseExceptionHandler(exceptionApp =>
        {
            exceptionApp.Run(async context =>
            {
                context.Response.ContentType = "application/problem+json";

                var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
                var error = exceptionFeature?.Error;
                var correlationId = context.Items[CorrelationIdMiddleware.ItemKey]?.ToString();

                // PlatformException → use its declared status code and title
                // Everything else → 500 Internal Server Error
                var (statusCode, title, detail) = error switch
                {
                    PlatformException pe => (
                        pe.StatusCode,
                        pe.Title,
                        pe.Message),
                    _ => (
                        StatusCodes.Status500InternalServerError,
                        "An unexpected error occurred",
                        app.Environment.IsDevelopment()
                            ? error?.Message ?? "Unknown error"
                            : "An internal error occurred. Use the correlation ID to find details in application logs.")
                };

                // Log at appropriate level: 4xx = Warning, 5xx = Error
                if (statusCode >= 500)
                {
                    Log.Error(error,
                        "Unhandled exception for {Method} {Path} (CorrelationId: {CorrelationId})",
                        context.Request.Method, context.Request.Path, correlationId);
                }
                else
                {
                    Log.Warning(error,
                        "{Title} for {Method} {Path} (CorrelationId: {CorrelationId}): {Detail}",
                        title, context.Request.Method, context.Request.Path, correlationId, detail);
                }

                var problem = new ProblemDetails
                {
                    Status = statusCode,
                    Title = title,
                    Detail = detail,
                    Instance = context.Request.Path,
                };
                problem.Extensions["correlationId"] = correlationId;

                // Add downstream service info for Bad Gateway errors
                if (error is BadGatewayException bgEx)
                {
                    problem.Extensions["downstreamService"] = bgEx.DownstreamService;
                }

                context.Response.StatusCode = statusCode;
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

        // ── Health check endpoints ───────────────────────────────────────
        // /healthz/live  → liveness:  is the process alive? (k8s livenessProbe)
        //                   If this fails, k8s restarts the pod.
        //                   Only checks the "self" check — no dependencies.
        //
        // /healthz/ready → readiness: can this instance serve traffic? (k8s readinessProbe)
        //                   If this fails, k8s removes the pod from the load balancer.
        //                   Checks dependencies (Key Vault, databases, etc.).
        app.MapHealthChecks("/healthz/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            ResponseWriter = WriteHealthResponse,
        });

        app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthResponse,
        });

        return app;
    }

    private static async Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var result = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.ToString(),
                description = e.Value.Description,
                exception = e.Value.Exception?.Message,
            }),
        };

        await context.Response.WriteAsJsonAsync(result);
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
