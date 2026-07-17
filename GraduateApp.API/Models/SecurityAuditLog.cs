namespace GraduateApp.API.Models;

public sealed class SecurityAuditLog
{
    public long AuditId { get; set; }
    public int? ActorAdminId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
