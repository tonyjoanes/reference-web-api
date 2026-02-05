using System.ComponentModel.DataAnnotations;

namespace ReferenceWebApi.Configuration;

/// <summary>
/// Options for connecting to Azure App Configuration.
/// Bound from the "AzureAppConfiguration" section.
/// </summary>
public class AzureAppConfigurationOptions
{
    public const string SectionName = "AzureAppConfiguration";

    /// <summary>
    /// The Azure App Configuration endpoint URL.
    /// When empty, Azure App Configuration is skipped.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Sentinel key used to trigger configuration refresh.
    /// When this key's value changes, all configuration is reloaded.
    /// </summary>
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
}
