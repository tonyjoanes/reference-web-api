using System.ComponentModel.DataAnnotations;

namespace ReferenceWebApi.Configuration;

/// <summary>
/// General API options bound from the "Api" configuration section.
/// </summary>
public class ApiOptions
{
    public const string SectionName = "Api";

    [Required(AllowEmptyStrings = false)]
    [StringLength(100, MinimumLength = 1)]
    public string Title { get; set; } = "Reference Web API";

    [Required(AllowEmptyStrings = false)]
    [RegularExpression(@"^v\d+(\.\d+)?$", ErrorMessage = "Version must match pattern 'v1' or 'v1.0'")]
    public string Version { get; set; } = "v1";

    public bool EnableSwagger { get; set; } = true;
}
