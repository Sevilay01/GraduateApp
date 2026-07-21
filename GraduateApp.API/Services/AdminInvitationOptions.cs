namespace GraduateApp.API.Services;

public sealed class AdminInvitationOptions
{
    public const string SectionName = "AdminInvitation";

    public int LifetimeHours { get; set; } = 24;
}
