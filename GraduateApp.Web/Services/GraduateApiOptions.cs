namespace GraduateApp.Web.Services;

public sealed class GraduateApiOptions
{
    public const string SectionName = "GraduateApi";

    public string BaseAddress { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 15;
    public int InnerDependencyTimeoutSeconds { get; init; } = 8;
}
