namespace ReferenceWebApi.Configuration;

/// <summary>
/// General API options bound from the "Api" configuration section.
/// </summary>
public class ApiOptions
{
    public const string SectionName = "Api";

    public string Title { get; set; } = "Reference Web API";
    public string Version { get; set; } = "v1";
    public bool EnableSwagger { get; set; } = true;
}
