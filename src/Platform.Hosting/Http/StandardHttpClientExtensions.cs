using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Platform.Hosting.Http;

/// <summary>
/// Extension methods for registering HttpClient instances with the platform's
/// standard resilience policies (retry with exponential backoff and circuit
/// breaker) and automatic correlation ID propagation.
/// </summary>
public static class StandardHttpClientExtensions
{
    /// <summary>
    /// Registers a named HttpClient with the platform's standard resilience
    /// pipeline: correlation ID propagation, exponential-backoff retry for
    /// transient failures, and a circuit breaker.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">A logical name for this HTTP client (e.g. "payments-api").</param>
    /// <param name="configureClient">Optional: configure the base address, default headers, etc.</param>
    /// <returns>The IHttpClientBuilder for further customisation.</returns>
    public static IHttpClientBuilder AddStandardHttpClient(
        this IServiceCollection services,
        string name,
        Action<HttpClient>? configureClient = null)
    {
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdDelegatingHandler>();

        var clientBuilder = services.AddHttpClient(name, client =>
            {
                configureClient?.Invoke(client);
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddStandardResilienceHandler();

        return clientBuilder;
    }

    /// <summary>
    /// Registers a typed HttpClient with the platform's standard resilience pipeline.
    /// </summary>
    public static IHttpClientBuilder AddStandardHttpClient<TClient>(
        this IServiceCollection services,
        Action<HttpClient>? configureClient = null)
        where TClient : class
    {
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdDelegatingHandler>();

        var clientBuilder = services.AddHttpClient<TClient>(client =>
            {
                configureClient?.Invoke(client);
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddStandardResilienceHandler();

        return clientBuilder;
    }
}
