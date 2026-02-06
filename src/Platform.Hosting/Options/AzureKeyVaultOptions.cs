using System.ComponentModel.DataAnnotations;

namespace Platform.Hosting.Options;

/// <summary>
/// Options for connecting to Azure Key Vault.
/// Bound from the "AzureKeyVault" configuration section.
/// </summary>
public class AzureKeyVaultOptions : IValidatableObject
{
    public const string SectionName = "AzureKeyVault";

    /// <summary>
    /// The Key Vault URI (e.g. "https://my-vault.vault.azure.net/").
    /// When empty, Key Vault configuration is skipped.
    /// </summary>
    [Url(ErrorMessage = "VaultUri must be a valid URL when provided")]
    public string VaultUri { get; set; } = string.Empty;

    /// <summary>
    /// Optional prefix for secrets. Secrets named "prefix--key" are mapped
    /// to configuration key "key" (double-dash becomes colon separator).
    /// </summary>
    public string SecretPrefix { get; set; } = string.Empty;

    /// <summary>
    /// How long secrets are cached before reloading from Key Vault.
    /// </summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(30);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(VaultUri);

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

        if (IsConfigured && !VaultUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            yield return new ValidationResult(
                "VaultUri must use HTTPS",
                [nameof(VaultUri)]);
    }
}
