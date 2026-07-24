using GraduateApp.API.Domain;

namespace GraduateApp.API.Models;

public sealed class ProgramOfferingEvaluationCriterion
{
    public int CriterionId { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public int ProgramOfferingId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NormalizedCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public EvaluationCriterionSourceType SourceType { get; set; }
    public int? ExamId { get; set; }
    public int WeightBasisPoints { get; set; }
    public decimal MaximumRawScore { get; set; }
    public int TieBreakPriority { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ProgramOffering ProgramOffering { get; set; } = null!;
    public Exam? Exam { get; set; }
    public ICollection<ApplicationEvaluationComponent> Components { get; set; } = new List<ApplicationEvaluationComponent>();
}
