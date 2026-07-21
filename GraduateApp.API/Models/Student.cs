namespace GraduateApp.API.Models;

public sealed class Student
{
    public string Tc { get; set; } = string.Empty;
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public string StudentName { get; set; } = string.Empty;
    public string StudentSurname { get; set; } = string.Empty;
    public string? FatherName { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string? Telephone { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsActive { get; set; } = true;
    public int AccessFailedCount { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();
    public ICollection<EducationInfo> EducationInfos { get; set; } = new List<EducationInfo>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    public ICollection<StudentExamScore> StudentExamScores { get; set; } = new List<StudentExamScore>();
    public ICollection<SystemLog> SystemLogs { get; set; } = new List<SystemLog>();
    public LoginIdentity? LoginIdentity { get; set; }
}
