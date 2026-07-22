using GraduateApp.API.Domain;

namespace GraduateApp.API.Models;

public sealed class ApplicationEvaluationComponent
{
    public int ComponentId { get; set; }
    public int ApplicationEvaluationId { get; set; }
    public int? SourceCriterionId { get; set; }
    public Guid CriterionPublicIdSnapshot { get; set; }
    public string CodeSnapshot { get; set; } = string.Empty;
    public string DisplayNameSnapshot { get; set; } = string.Empty;
    public EvaluationCriterionSourceType SourceTypeSnapshot { get; set; }
    public int? ExamId { get; set; }
    public decimal? RawScore { get; set; }
    public decimal MaximumRawScoreSnapshot { get; set; }
    public decimal? NormalizedScore { get; set; }
    public int WeightBasisPointsSnapshot { get; set; }
    public decimal? WeightedScore { get; set; }
    public int TieBreakPrioritySnapshot { get; set; }
    public int? ManualScoredByAdminId { get; set; }
    public DateTime? ManualScoredAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ApplicationEvaluation ApplicationEvaluation { get; set; } = null!;
    public ProgramOfferingEvaluationCriterion? SourceCriterion { get; set; }
    public Admin? ManualScoredByAdmin { get; set; }
}
