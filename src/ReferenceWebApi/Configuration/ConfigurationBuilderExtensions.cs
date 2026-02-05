using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Serilog;

namespace ReferenceWebApi.Configuration;

/// <summary>
/// Extension methods for building configuration with Azure App Configuration
/// and Azure Key Vault, with diagnostic logging at each step.
/// </summary>
public static class ConfigurationBuilderExtensions
{
    /// <summary>
    /// Adds all configuration sources in the correct order of precedence:
    ///
    ///   1. appsettings.json                  (base defaults)
    ///   2. appsettings.{Environment}.json    (environment overrides)
    ///   3. User secrets                       (Development only)
    ///   4. Azure App Configuration            (centralised config)
    ///   5. Azure Key Vault                    (secrets)
    ///   6. Environment variables              (deployment overrides)
    ///   7. Command-line arguments             (ad-hoc overrides)
    ///
    /// Later sources override earlier ones. This means a value in Key Vault
    /// overrides the same key from App Configuration, which overrides
    /// appsettings.json, etc.
    /// </summary>
    public static IConfigurationBuilder AddConfigurationSources(
        this IConfigurationBuilder builder,
        string[] args,
        string environment)
    {
        // ── Step 1 & 2: JSON files (already added by default host builder) ──
        Log.Information("Configuration source [1/7]: appsettings.json");
        Log.Information("Configuration source [2/7]: appsettings.{Environment}.json", environment);

        // ── Step 3: User secrets (Development only) ──
        if (string.Equals(environment, Environments.Development, StringComparison.OrdinalIgnoreCase))
        {
            Log.Information("Configuration source [3/7]: User secrets (Development)");
        }
        else
        {
            Log.Debug("Configuration source [3/7]: User secrets — skipped (not Development)");
        }

        // Build an intermediate configuration so we can read Azure connection
        // settings from appsettings / env vars before adding Azure sources.
        var intermediateConfig = builder.Build();

        // ── Step 4: Azure App Configuration ──
        builder.AddAzureAppConfiguration(intermediateConfig, environment);

        // ── Step 5: Azure Key Vault ──
        builder.AddAzureKeyVault(intermediateConfig);

        // ── Step 6: Environment variables ──
        builder.AddEnvironmentVariables();
        Log.Information("Configuration source [6/7]: Environment variables");

        // ── Step 7: Command-line arguments ──
        if (args.Length > 0)
        {
            builder.AddCommandLine(args);
            Log.Information("Configuration source [7/7]: Command-line arguments ({Count} args)", args.Length);
        }
        else
        {
            Log.Debug("Configuration source [7/7]: Command-line arguments — none provided");
        }

        return builder;
    }

    private static IConfigurationBuilder AddAzureAppConfiguration(
        this IConfigurationBuilder builder,
        IConfiguration intermediateConfig,
        string environment)
    {
        var options = new AzureAppConfigurationOptions();
        intermediateConfig.GetSection(AzureAppConfigurationOptions.SectionName).Bind(options);

        if (!options.IsConfigured)
        {
            Log.Warning(
                "Configuration source [4/7]: Azure App Configuration — skipped " +
                "(no endpoint configured). Set {Section}:{Key} to enable",
                AzureAppConfigurationOptions.SectionName, nameof(AzureAppConfigurationOptions.Endpoint));
            return builder;
        }

        Log.Information(
            "Configuration source [4/7]: Azure App Configuration — connecting to {Endpoint}",
            options.Endpoint);

        try
        {
            var credential = new DefaultAzureCredential();

            builder.AddAzureAppConfiguration(azureOptions =>
            {
                azureOptions.Connect(new Uri(options.Endpoint), credential);

                // Select() is additive. Keys with no label serve as the base;
                // labelled keys override them for the same key name.
                if (!string.IsNullOrWhiteSpace(options.KeyFilter))
                {
                    azureOptions.Select(options.KeyFilter, LabelFilter.Null);
                    Log.Information("  Azure App Config: selecting '{KeyFilter}' (no label)", options.KeyFilter);
                }

                if (!string.IsNullOrWhiteSpace(options.LabelFilter))
                {
                    var filter = string.IsNullOrWhiteSpace(options.KeyFilter)
                        ? KeyFilter.Any
                        : options.KeyFilter;
                    azureOptions.Select(filter, options.LabelFilter);
                    Log.Information(
                        "  Azure App Config: selecting with label '{Label}' (overrides no-label)",
                        options.LabelFilter);
                }

                azureOptions.ConfigureRefresh(refresh =>
                {
                    refresh
                        .Register(options.SentinelKey, refreshAll: true)
                        .SetCacheExpiration(options.CacheExpiration);
                });

                Log.Information(
                    "  Azure App Config: sentinel = '{Sentinel}', cache = {CacheExpiration}",
                    options.SentinelKey, options.CacheExpiration);
            });

            Log.Information("  Azure App Configuration connected successfully");
        }
        catch (UriFormatException ex)
        {
            Log.Error(ex,
                "Configuration source [4/7]: Azure App Configuration — invalid endpoint URI '{Endpoint}'",
                options.Endpoint);
        }
        catch (AuthenticationFailedException ex)
        {
            Log.Error(ex,
                "Configuration source [4/7]: Azure App Configuration — authentication failed for {Endpoint}. " +
                "Verify DefaultAzureCredential is configured. Error: {Error}",
                options.Endpoint, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "Configuration source [4/7]: Azure App Configuration — FAILED to connect to {Endpoint}. " +
                "The application will continue without it. Error: {Error}",
                options.Endpoint, ex.Message);
        }

        return builder;
    }

    private static IConfigurationBuilder AddAzureKeyVault(
        this IConfigurationBuilder builder,
        IConfiguration intermediateConfig)
    {
        var options = new AzureKeyVaultOptions();
        intermediateConfig.GetSection(AzureKeyVaultOptions.SectionName).Bind(options);

        if (!options.IsConfigured)
        {
            Log.Warning(
                "Configuration source [5/7]: Azure Key Vault — skipped " +
                "(no vault URI configured). Set {Section}:{Key} to enable",
                AzureKeyVaultOptions.SectionName, nameof(AzureKeyVaultOptions.VaultUri));
            return builder;
        }

        Log.Information(
            "Configuration source [5/7]: Azure Key Vault — connecting to {VaultUri}",
            options.VaultUri);

        try
        {
            var credential = new DefaultAzureCredential();
            var vaultUri = new Uri(options.VaultUri);

            builder.AddAzureKeyVault(
                vaultUri,
                credential,
                new PrefixKeyVaultSecretManager(options.SecretPrefix));

            Log.Information(
                "  Key Vault: prefix = '{Prefix}', cache = {CacheExpiration}",
                options.SecretPrefix, options.CacheExpiration);
            Log.Information("  Azure Key Vault connected successfully");
        }
        catch (UriFormatException ex)
        {
            Log.Error(ex,
                "Configuration source [5/7]: Azure Key Vault — invalid vault URI '{VaultUri}'",
                options.VaultUri);
        }
        catch (AuthenticationFailedException ex)
        {
            Log.Error(ex,
                "Configuration source [5/7]: Azure Key Vault — authentication failed for {VaultUri}. " +
                "Verify DefaultAzureCredential is configured. Error: {Error}",
                options.VaultUri, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "Configuration source [5/7]: Azure Key Vault — FAILED to connect to {VaultUri}. " +
                "The application will continue without it. Error: {Error}",
                options.VaultUri, ex.Message);
        }

        return builder;
    }
}
