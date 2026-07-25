namespace GraduateApp.API.Infrastructure;

public sealed class DatabaseTimeoutOptions
{
    public const string SectionName = "DatabaseTimeouts";

    public int ConnectionSeconds { get; init; } = 5;
    public int CommandSeconds { get; init; } = 8;
    public int ReadinessSeconds { get; init; } = 3;
}
