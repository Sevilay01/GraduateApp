namespace GraduateApp.API.Models;

public sealed class PasswordResetToken
{
    public int TokenId { get; set; }
    public string? Tc { get; set; }
    public int? AdminId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpirationDate { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Student? TcNavigation { get; set; }
    public Admin? Admin { get; set; }
}
