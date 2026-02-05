namespace ReferenceWebApi.Middleware;

/// <summary>
/// Propagates or generates a correlation ID for every request.
/// Reads from the incoming X-Correlation-ID header (set by API gateways,
/// upstream services, etc.) and falls back to a new GUID.
/// The value is placed on the response header and into the Serilog
/// LogContext so every log line within the request is correlated.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
                            ?? Guid.NewGuid().ToString("D");

        context.Items["CorrelationId"] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Push into Serilog LogContext so every log line includes it
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            _logger.LogDebug("Request correlated as {CorrelationId}", correlationId);
            await _next(context);
        }
    }
}
