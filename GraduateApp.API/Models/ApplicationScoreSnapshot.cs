namespace GraduateApp.API.Models;

public sealed class ApplicationScoreSnapshot
{
    public int ApplicationScoreSnapshotId { get; set; }
    public int ApplicationId { get; set; }
    public int ExamId { get; set; }
    public string ExamNameSnapshot { get; set; } = string.Empty;
    public decimal ScoreSnapshot { get; set; }
    public DateOnly? ExamDateSnapshot { get; set; }
    public DateTime CapturedAtUtc { get; set; }

    public Application Application { get; set; } = null!;
    public Exam Exam { get; set; } = null!;
}
