namespace Platform.Hosting.Exceptions;

/// <summary>
/// Base class for domain exceptions that map to specific HTTP status codes.
/// Throw these from your services/handlers and the platform exception handler
/// will automatically return the correct RFC 7807 Problem Details response.
///
/// This prevents every controller from having its own try/catch patterns
/// and ensures consistent error responses across all services.
/// </summary>
public abstract class PlatformException : Exception
{
    /// <summary>The HTTP status code this exception maps to.</summary>
    public abstract int StatusCode { get; }

    /// <summary>
    /// A short, human-readable title for this error category
    /// (e.g. "Not Found", "Conflict"). Appears as the ProblemDetails.Title.
    /// </summary>
    public abstract string Title { get; }

    protected PlatformException(string message) : base(message) { }
    protected PlatformException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>404 Not Found — the requested resource does not exist.</summary>
public class NotFoundException : PlatformException
{
    public override int StatusCode => 404;
    public override string Title => "Not Found";

    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string entity, object id)
        : base($"{entity} with ID '{id}' was not found") { }
}

/// <summary>400 Bad Request — the request is malformed or invalid.</summary>
public class BadRequestException : PlatformException
{
    public override int StatusCode => 400;
    public override string Title => "Bad Request";

    public BadRequestException(string message) : base(message) { }
    public BadRequestException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>409 Conflict — the request conflicts with current state.</summary>
public class ConflictException : PlatformException
{
    public override int StatusCode => 409;
    public override string Title => "Conflict";

    public ConflictException(string message) : base(message) { }
}

/// <summary>403 Forbidden — the caller is authenticated but not authorized.</summary>
public class ForbiddenException : PlatformException
{
    public override int StatusCode => 403;
    public override string Title => "Forbidden";

    public ForbiddenException(string message) : base(message) { }
}

/// <summary>422 Unprocessable Entity — the request is well-formed but semantically invalid.</summary>
public class UnprocessableException : PlatformException
{
    public override int StatusCode => 422;
    public override string Title => "Unprocessable Entity";

    public UnprocessableException(string message) : base(message) { }
}

/// <summary>
/// 502 Bad Gateway — a downstream service returned an unexpected response.
/// Use this when your service is a proxy or orchestrator and an upstream call fails.
/// </summary>
public class BadGatewayException : PlatformException
{
    public override int StatusCode => 502;
    public override string Title => "Bad Gateway";

    public string DownstreamService { get; }

    public BadGatewayException(string downstreamService, string message)
        : base(message)
    {
        DownstreamService = downstreamService;
    }

    public BadGatewayException(string downstreamService, string message, Exception innerException)
        : base(message, innerException)
    {
        DownstreamService = downstreamService;
    }
}
