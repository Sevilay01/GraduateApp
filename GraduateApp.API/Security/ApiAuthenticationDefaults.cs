namespace GraduateApp.API.Security;

public static class ApiAuthenticationDefaults
{
    public const string Scheme = "GraduateAppBearer";
    public const string StudentRole = "Student";
    public const string AdminRole = "Admin";
    public const string SecurityStampClaim = "security_stamp";
}
