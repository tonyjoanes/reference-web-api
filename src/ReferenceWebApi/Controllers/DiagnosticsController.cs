using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Platform.Hosting.Options;
using ReferenceWebApi.Configuration;

namespace ReferenceWebApi.Controllers;

/// <summary>
/// Diagnostic endpoints for inspecting the running configuration.
/// Restricted to Development environment only — returns 404 in Production
/// so the endpoint doesn't even appear to exist.
/// </summary>
[ApiController]
[Route("[controller]")]
public class DiagnosticsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DiagnosticsController> _logger;
    private readonly IOptionsSnapshot<ApiOptions> _apiOptions;
    private readonly IOptionsSnapshot<AzureAppConfigurationOptions> _appConfigOptions;
    private readonly IOptionsSnapshot<AzureKeyVaultOptions> _keyVaultOptions;

    public DiagnosticsController(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<DiagnosticsController> logger,
        IOptionsSnapshot<ApiOptions> apiOptions,
        IOptionsSnapshot<AzureAppConfigurationOptions> appConfigOptions,
        IOptionsSnapshot<AzureKeyVaultOptions> keyVaultOptions)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
        _apiOptions = apiOptions;
        _appConfigOptions = appConfigOptions;
        _keyVaultOptions = keyVaultOptions;
    }

    /// <summary>
    /// Returns the list of configuration providers in precedence order,
    /// and reports which Azure services are connected.
    /// Does NOT expose any configuration values — only provider types and
    /// connection metadata.
    /// Only available in Development.
    /// </summary>
    [HttpGet("config/providers")]
    public IActionResult GetConfigurationProviders()
    {
        if (!_environment.IsDevelopment())
            return NotFound();

        _logger.LogInformation("Diagnostics: listing configuration providers");

        var providers = new List<object>();

        if (_configuration is IConfigurationRoot configRoot)
        {
            var providerList = configRoot.Providers.ToList();
            for (var i = 0; i < providerList.Count; i++)
            {
                providers.Add(new
                {
                    Order = i + 1,
                    Provider = providerList[i].GetType().Name,
                });
            }
        }

        return Ok(new
        {
            Environment = _environment.EnvironmentName,
            ContentRoot = _environment.ContentRootPath,
            ProviderCount = providers.Count,
            Providers = providers,
            AzureAppConfiguration = new
            {
                _appConfigOptions.Value.IsConfigured,
                Endpoint = MaskUri(_appConfigOptions.Value.Endpoint),
                _appConfigOptions.Value.KeyFilter,
                _appConfigOptions.Value.SentinelKey,
                _appConfigOptions.Value.CacheExpiration,
            },
            AzureKeyVault = new
            {
                _keyVaultOptions.Value.IsConfigured,
                VaultUri = MaskUri(_keyVaultOptions.Value.VaultUri),
                _keyVaultOptions.Value.SecretPrefix,
                _keyVaultOptions.Value.CacheExpiration,
            },
            Api = _apiOptions.Value,
        });
    }

    /// <summary>
    /// Lists the configuration keys (not values) to help diagnose
    /// which settings are present. Only available in Development.
    /// </summary>
    [HttpGet("config/keys")]
    public IActionResult GetConfigurationKeys([FromQuery] string? prefix)
    {
        if (!_environment.IsDevelopment())
            return NotFound();

        _logger.LogInformation("Diagnostics: listing configuration keys with prefix '{Prefix}'", prefix ?? "(all)");

        var children = string.IsNullOrWhiteSpace(prefix)
            ? _configuration.GetChildren()
            : _configuration.GetSection(prefix).GetChildren();

        var keys = children
            .Select(c => c.Path)
            .OrderBy(k => k)
            .ToList();

        return Ok(new
        {
            Prefix = prefix ?? "(root)",
            Count = keys.Count,
            Keys = keys,
        });
    }

    /// <summary>
    /// Masks a URI to show only the host, hiding the full path and query.
    /// </summary>
    private static string MaskUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return "(not configured)";

        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return $"{parsed.Scheme}://{parsed.Host}/***";

        return "(invalid URI)";
    }
}
