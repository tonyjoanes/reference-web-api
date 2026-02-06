using Azure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.Hosting.Internal;
using Platform.Hosting.Options;
using Serilog;

namespace Platform.Hosting;

/// <summary>
/// Opinionated extension methods that configure a WebApplicationBuilder
/// with the organisation's standard configuration sources, Serilog,
/// options validation, and health checks.
///
/// <para>
/// Configuration sources are loaded in a fixed order of precedence (last wins):
/// </para>
///
/// <list type="number">
///   <item><description>appsettings.json (base defaults)</description></item>
///   <item><description>appsettings.{Environment}.json (environment overrides)</description></item>
///   <item><description>User secrets (Development only)</description></item>
///   <item><description>Azure App Configuration (centralised config)</description></item>
///   <item><description>Azure Key Vault (secrets)</description></item>
///   <item><description>Environment variables (deployment overrides)</description></item>
///   <item><description>Command-line arguments (ad-hoc overrides)</description></item>
/// </list>
///
/// <para>
/// This ordering is enforced by the library and cannot be changed by
/// consuming services. This prevents the class of bug where "it works on
/// my machine" because one service loads env vars before Key Vault and
/// another does the opposite.
/// </para>
/// </summary>
public static class StandardConfigurationExtensions
{
    /// <summary>
    /// Configures the standard configuration pipeline, Serilog, options
    /// validation, and health checks. Call this once in Program.cs.
    /// </summary>
    /// <param name="builder">The WebApplicationBuilder from CreateBuilder().</param>
    /// <param name="args">Command-line arguments (pass through from Main).</param>
    /// <returns>The same builder, for chaining.</returns>
    public static WebApplicationBuilder AddStandardConfiguration(
        this WebApplicationBuilder builder,
        string[] args)
    {
        var environment = builder.Environment.EnvironmentName;

        // ── Configuration sources ────────────────────────────────────────
        AddConfigurationSources(builder.Configuration, args, environment);

        // ── Serilog ──────────────────────────────────────────────────────
        builder.Host.UseSerilog((context, services, loggerConfig) =>
        {
            loggerConfig.ReadFrom.Configuration(context.Configuration);
            Log.Information("Serilog reconfigured from full configuration pipeline");
        });

        // ── Options with validation ──────────────────────────────────────
        builder.Services
            .AddOptions<AzureAppConfigurationOptions>()
            .BindConfiguration(AzureAppConfigurationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services
            .AddOptions<AzureKeyVaultOptions>()
            .BindConfiguration(AzureKeyVaultOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Azure App Config refresh middleware service ───────────────────
        var appConfigOptions = new AzureAppConfigurationOptions();
        builder.Configuration
            .GetSection(AzureAppConfigurationOptions.SectionName)
            .Bind(appConfigOptions);

        if (appConfigOptions.IsConfigured)
        {
            builder.Services.AddAzureAppConfiguration();
        }

        // ── Problem Details (RFC 7807) ───────────────────────────────────
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["correlationId"] =
                    context.HttpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString();
            };
        });

        // ── Health checks ────────────────────────────────────────────────
        var healthChecks = builder.Services.AddHealthChecks();

        var kvOptions = new AzureKeyVaultOptions();
        builder.Configuration
            .GetSection(AzureKeyVaultOptions.SectionName)
            .Bind(kvOptions);

        if (kvOptions.IsConfigured)
        {
            healthChecks.AddAzureKeyVault(
                new Uri(kvOptions.VaultUri),
                new DefaultAzureCredential(),
                options => { options.AddSecret("health-check-probe"); },
                name: "azure-key-vault",
                tags: ["azure", "secrets"]);
            Log.Information("Health check registered: Azure Key Vault ({VaultUri})", kvOptions.VaultUri);
        }

        return builder;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private: configuration source ordering (the whole point of this lib)
    // ─────────────────────────────────────────────────────────────────────

    private static void AddConfigurationSources(
        IConfigurationBuilder builder,
        string[] args,
        string environment)
    {
        // Steps 1–3 (appsettings.json, appsettings.{env}.json, user secrets)
        // are already added by WebApplication.CreateBuilder(). We log them.
        Log.Information("Configuration source [1/7]: appsettings.json");
        Log.Information("Configuration source [2/7]: appsettings.{Environment}.json", environment);

        if (string.Equals(environment, Environments.Development, StringComparison.OrdinalIgnoreCase))
        {
            Log.Information("Configuration source [3/7]: User secrets (Development)");
        }
        else
        {
            Log.Debug("Configuration source [3/7]: User secrets — skipped (not Development)");
        }

        // Build intermediate config so we can read Azure settings from
        // appsettings / env vars before adding the Azure sources.
        var intermediateConfig = builder.Build();

        // Step 4: Azure App Configuration
        AddAzureAppConfiguration(builder, intermediateConfig, environment);

        // Step 5: Azure Key Vault
        AddAzureKeyVault(builder, intermediateConfig);

        // Step 6: Environment variables
        builder.AddEnvironmentVariables();
        Log.Information("Configuration source [6/7]: Environment variables");

        // Step 7: Command-line arguments
        if (args.Length > 0)
        {
            builder.AddCommandLine(args);
            Log.Information("Configuration source [7/7]: Command-line arguments ({Count} args)", args.Length);
        }
        else
        {
            Log.Debug("Configuration source [7/7]: Command-line arguments — none provided");
        }
    }

    private static void AddAzureAppConfiguration(
        IConfigurationBuilder builder,
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
            return;
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
    }

    private static void AddAzureKeyVault(
        IConfigurationBuilder builder,
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
            return;
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
    }
}
