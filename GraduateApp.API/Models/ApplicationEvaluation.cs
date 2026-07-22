using GraduateApp.API.Domain;

namespace GraduateApp.API.Models;

public sealed class ApplicationEvaluation
{
    public int ApplicationEvaluationId { get; set; }
    public int ApplicationId { get; set; }
    public int ProgramOfferingId { get; set; }
    public EvaluationEligibilityStatus EligibilityStatus { get; set; } = EvaluationEligibilityStatus.Pending;
    public string? IneligibilityReason { get; set; }
    public decimal? TotalScore { get; set; }
    public int? Rank { get; set; }
    public EvaluationOutcome? Outcome { get; set; }
    public int? EligibilityDecidedByAdminId { get; set; }
    public DateTime? EligibilityDecidedAtUtc { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Application Application { get; set; } = null!;
    public ProgramOffering ProgramOffering { get; set; } = null!;
    public Admin? EligibilityDecidedByAdmin { get; set; }
    public ICollection<ApplicationEvaluationComponent> Components { get; set; } = new List<ApplicationEvaluationComponent>();
}
