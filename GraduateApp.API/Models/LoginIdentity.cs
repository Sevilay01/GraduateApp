namespace GraduateApp.API.Models;

public enum LoginAccountType
{
    Student = 1,
    Admin = 2
}

public sealed class LoginIdentity
{
    public int LoginIdentityId { get; set; }
    public string NormalizedEmail { get; set; } = string.Empty;
    public LoginAccountType AccountType { get; set; }
    public string? StudentTc { get; set; }
    public int? AdminId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Student? Student { get; set; }
    public Admin? Admin { get; set; }
}
