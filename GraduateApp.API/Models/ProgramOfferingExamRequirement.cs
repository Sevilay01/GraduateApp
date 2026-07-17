namespace GraduateApp.API.Models;

public sealed class ProgramOfferingExamRequirement
{
    public int RequirementId { get; set; }
    public int ProgramOfferingId { get; set; }
    public int ExamId { get; set; }
    public decimal MinimumScore { get; set; }
    public DateOnly? MinimumValidityDate { get; set; }
    public bool IsRequired { get; set; }

    public ProgramOffering ProgramOffering { get; set; } = null!;
    public Exam Exam { get; set; } = null!;
}
