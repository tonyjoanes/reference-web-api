using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ReferenceWebApi.Configuration;

namespace ReferenceWebApi.Controllers;

/// <summary>
/// Diagnostic endpoints for inspecting the running configuration.
/// In production, protect these endpoints with authorization.
/// </summary>
[ApiController]
[Route("[controller]")]
public class DiagnosticsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DiagnosticsController> _logger;
    private readonly IOptions<ApiOptions> _apiOptions;
    private readonly IOptions<AzureAppConfigurationOptions> _appConfigOptions;
    private readonly IOptions<AzureKeyVaultOptions> _keyVaultOptions;

    public DiagnosticsController(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<DiagnosticsController> logger,
        IOptions<ApiOptions> apiOptions,
        IOptions<AzureAppConfigurationOptions> appConfigOptions,
        IOptions<AzureKeyVaultOptions> keyVaultOptions)
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
    /// Does NOT expose any configuration values.
    /// </summary>
    [HttpGet("config/providers")]
    public IActionResult GetConfigurationProviders()
    {
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
                _appConfigOptions.Value.Endpoint,
                _appConfigOptions.Value.KeyFilter,
                _appConfigOptions.Value.SentinelKey,
                _appConfigOptions.Value.CacheExpiration,
            },
            AzureKeyVault = new
            {
                _keyVaultOptions.Value.IsConfigured,
                _keyVaultOptions.Value.VaultUri,
                _keyVaultOptions.Value.SecretPrefix,
                _keyVaultOptions.Value.CacheExpiration,
            },
            Api = _apiOptions.Value,
        });
    }

    /// <summary>
    /// Lists the configuration keys (not values) to help diagnose
    /// which settings are present. Safe to call in non-production
    /// environments; in production, add authorization.
    /// </summary>
    [HttpGet("config/keys")]
    public IActionResult GetConfigurationKeys([FromQuery] string? prefix)
    {
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
}
