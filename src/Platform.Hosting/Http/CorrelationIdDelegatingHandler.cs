using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Hosting.Internal;

namespace Platform.Hosting.Http;

/// <summary>
/// Delegating handler that propagates the X-Correlation-ID from the
/// incoming request to all outgoing HttpClient calls. This ensures
/// distributed tracing continuity across service-to-service calls
/// without requiring each team to remember to forward the header.
/// </summary>
internal class CorrelationIdDelegatingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CorrelationIdDelegatingHandler> _logger;

    public CorrelationIdDelegatingHandler(
        IHttpContextAccessor httpContextAccessor,
        ILogger<CorrelationIdDelegatingHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var correlationId = _httpContextAccessor.HttpContext?
            .Items[CorrelationIdMiddleware.ItemKey]?.ToString();

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation(
                CorrelationIdMiddleware.HeaderName, correlationId);
            _logger.LogDebug(
                "Propagating {Header}: {CorrelationId} to outgoing request {Uri}",
                CorrelationIdMiddleware.HeaderName, correlationId, request.RequestUri);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
