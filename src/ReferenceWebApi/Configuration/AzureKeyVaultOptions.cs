namespace ReferenceWebApi.Configuration;

/// <summary>
/// Options for connecting to Azure Key Vault.
/// Bound from the "AzureKeyVault" section.
/// </summary>
public class AzureKeyVaultOptions
{
    public const string SectionName = "AzureKeyVault";

    /// <summary>
    /// The Key Vault URI (e.g. "https://my-vault.vault.azure.net/").
    /// When empty, Key Vault configuration is skipped.
    /// </summary>
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
}
