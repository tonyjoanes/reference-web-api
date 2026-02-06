using System.ComponentModel.DataAnnotations;

namespace Platform.Hosting.Options;

/// <summary>
/// Identifies the running service instance. Every service must declare
/// its identity so that logs, traces, metrics, and health checks can
/// be attributed. Bound from the "ServiceMetadata" configuration section.
/// </summary>
public class ServiceMetadataOptions
{
    public const string SectionName = "ServiceMetadata";

    /// <summary>
    /// Short, lowercase, hyphen-separated service name.
    /// Used as the OpenTelemetry service name, Serilog Application property,
    /// and health check display name (e.g. "order-api", "payment-worker").
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "ServiceMetadata:Name is required")]
    [RegularExpression(@"^[a-z][a-z0-9-]*$",
        ErrorMessage = "ServiceMetadata:Name must be lowercase, start with a letter, and use hyphens as separators")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Semantic version of the deployed service (e.g. "1.4.2", "2.0.0-rc.1").
    /// Typically injected at build time via CI/CD.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "ServiceMetadata:Version is required")]
    public string Version { get; set; } = "0.0.0-local";

    /// <summary>
    /// Team that owns this service. Shows up in health check metadata
    /// and can be used for alert routing.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "ServiceMetadata:Team is required")]
    public string Team { get; set; } = string.Empty;
}
