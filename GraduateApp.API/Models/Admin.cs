namespace GraduateApp.API.Models;

public sealed class Admin
{
    public int AdminId { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public int AccessFailedCount { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public bool MustChangePassword { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public bool IsInvitationPending { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<ApplicationStatusHistory> ApplicationStatusHistories { get; set; } = new List<ApplicationStatusHistory>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    public ICollection<SystemLog> SystemLogs { get; set; } = new List<SystemLog>();
    public LoginIdentity? LoginIdentity { get; set; }
}
