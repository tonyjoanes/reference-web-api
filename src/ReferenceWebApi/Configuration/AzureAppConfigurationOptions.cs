using System.ComponentModel.DataAnnotations;

namespace ReferenceWebApi.Configuration;

/// <summary>
/// Options for connecting to Azure App Configuration.
/// Bound from the "AzureAppConfiguration" section.
/// </summary>
public class AzureAppConfigurationOptions : IValidatableObject
{
    public const string SectionName = "AzureAppConfiguration";

    /// <summary>
    /// The Azure App Configuration endpoint URL.
    /// When empty, Azure App Configuration is skipped.
    /// </summary>
    [Url(ErrorMessage = "Endpoint must be a valid URL when provided")]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Sentinel key used to trigger configuration refresh.
    /// When this key's value changes, all configuration is reloaded.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string SentinelKey { get; set; } = "ReferenceWebApi:Sentinel";

    /// <summary>
    /// How long configuration values are cached before checking for updates.
    /// </summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Filter to select which keys to load (e.g. "ReferenceWebApi:*").
    /// </summary>
    public string KeyFilter { get; set; } = string.Empty;

    /// <summary>
    /// Label filter for Azure App Configuration (e.g. environment name).
    /// Empty string means no label filter (loads keys with no label).
    /// </summary>
    public string LabelFilter { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CacheExpiration < TimeSpan.FromSeconds(1))
            yield return new ValidationResult(
                "CacheExpiration must be at least 1 second",
                [nameof(CacheExpiration)]);

        if (CacheExpiration > TimeSpan.FromDays(1))
            yield return new ValidationResult(
                "CacheExpiration must not exceed 1 day",
                [nameof(CacheExpiration)]);

        if (IsConfigured && string.IsNullOrWhiteSpace(SentinelKey))
            yield return new ValidationResult(
                "SentinelKey is required when Endpoint is configured",
                [nameof(SentinelKey)]);
    }
}
