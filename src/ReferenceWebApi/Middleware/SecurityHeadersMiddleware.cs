namespace ReferenceWebApi.Middleware;

/// <summary>
/// Adds standard security headers to every response.
/// These headers defend against common attacks (XSS, clickjacking,
/// MIME sniffing) and are expected by security scanners.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["X-XSS-Protection"] = "0"; // Modern best practice: rely on CSP instead
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Content-Security-Policy"] = "default-src 'self'";
            headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), microphone=()";

            // Remove header that leaks server technology
            headers.Remove("X-Powered-By");
            headers.Remove("Server");

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
