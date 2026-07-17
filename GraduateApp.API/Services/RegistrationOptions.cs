namespace GraduateApp.API.Services;

public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";

    public int MinimumAge { get; set; } = 18;
    public int MaximumAge { get; set; } = 100;
    public string TimeZoneId { get; set; } = "Europe/Istanbul";
}
