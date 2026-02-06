using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace Platform.Hosting.Internal;

/// <summary>
/// Maps Key Vault secrets to configuration keys using an optional prefix.
///
/// Secrets are named with double dashes as separators (Key Vault doesn't allow colons).
/// For example, the secret "MyService--ConnectionStrings--Sql" maps to
/// the configuration key "ConnectionStrings:Sql" when the prefix is "MyService".
///
/// Secrets that don't start with the prefix are ignored.
/// </summary>
internal class PrefixKeyVaultSecretManager : KeyVaultSecretManager
{
    private readonly string _prefix;

    public PrefixKeyVaultSecretManager(string prefix)
    {
        _prefix = prefix;
    }

    public override bool Load(SecretProperties properties)
    {
        if (string.IsNullOrWhiteSpace(_prefix))
            return true;

        return properties.Name.StartsWith(_prefix + "--", StringComparison.OrdinalIgnoreCase);
    }

    public override string GetKey(KeyVaultSecret secret)
    {
        var name = secret.Name;

        if (!string.IsNullOrWhiteSpace(_prefix) &&
            name.StartsWith(_prefix + "--", StringComparison.OrdinalIgnoreCase))
        {
            name = name[((_prefix + "--").Length)..];
        }

        // Key Vault uses "--" where configuration uses ":"
        return name.Replace("--", ConfigurationPath.KeyDelimiter);
    }
}
