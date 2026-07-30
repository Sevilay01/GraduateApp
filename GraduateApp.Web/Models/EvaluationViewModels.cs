using System.ComponentModel.DataAnnotations;
using GraduateApp.Web.Validation;

namespace GraduateApp.Web.Models;

public enum EvaluationCriterionSourceType
{
    UndergraduateGpa,
    ExamScore,
    ManualScore
}

public enum EvaluationEligibilityStatus
{
    Pending,
    Eligible,
    Ineligible
}

public enum EvaluationOutcome
{
    Admitted,
    NotAdmitted,
    Ineligible
}

public enum OfferingEvaluationState
{
    Configuring,
    Finalized,
    Published
}

public static class EvaluationPresentation
{
    public static string DisplayName(this EvaluationCriterionSourceType sourceType) => sourceType switch
    {
        EvaluationCriterionSourceType.ExamScore => "Sınav puanı",
        EvaluationCriterionSourceType.UndergraduateGpa => "Lisans GNO",
        EvaluationCriterionSourceType.ManualScore => "Manuel puan",
        _ => "Bilinmiyor"
    };

    public static string DisplayName(this EvaluationOutcome outcome) => outcome switch
    {
        EvaluationOutcome.Admitted => "Kabul",
        EvaluationOutcome.NotAdmitted => "Kontenjan dışında",
        EvaluationOutcome.Ineligible => "Uygun bulunmadı",
        _ => "Bilinmiyor"
    };

    public static string DisplayName(this EvaluationEligibilityStatus status) => status switch
    {
        EvaluationEligibilityStatus.Pending => "Karar bekliyor",
        EvaluationEligibilityStatus.Eligible => "Uygun",
        EvaluationEligibilityStatus.Ineligible => "Uygun değil",
        _ => "Bilinmiyor"
    };

    public static string DisplayName(this OfferingEvaluationState state) => state switch
    {
        OfferingEvaluationState.Configuring => "Yapılandırılıyor",
        OfferingEvaluationState.Finalized => "Kesinleştirildi",
        OfferingEvaluationState.Published => "Yayımlandı",
        _ => "Bilinmiyor"
    };
}

public sealed class EvaluationCriterionViewModel
{
    public Guid PublicId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public EvaluationCriterionSourceType SourceType { get; set; }
    public int? ExamId { get; set; }
    public string? ExamName { get; set; }
    public int WeightBasisPoints { get; set; }
    public decimal MaximumRawScore { get; set; }
    public int TieBreakPriority { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class EvaluationCriterionFormViewModel
{
    public int ProgramOfferingId { get; set; }
    public Guid PublicId { get; set; }

    [Required, StringLength(64, MinimumLength = 2)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    public EvaluationCriterionSourceType SourceType { get; set; }
    public int? ExamId { get; set; }

    [Range(1, 10000)]
    public int WeightBasisPoints { get; set; }

    [LocalizedDecimalRange(
        "0.0001",
        "99999",
        ErrorMessage = "Maksimum ham puan 0,0001 ile 99999 arasında geçerli bir ondalık sayı olmalıdır.")]
    public decimal MaximumRawScore { get; set; }

    [Range(1, int.MaxValue)]
    public int TieBreakPriority { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class EvaluationComponentViewModel
{
    public Guid CriterionPublicId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public EvaluationCriterionSourceType SourceType { get; set; }
    public decimal? RawScore { get; set; }
    public decimal MaximumRawScore { get; set; }
    public decimal? NormalizedScore { get; set; }
    public int WeightBasisPoints { get; set; }
    public decimal? WeightedScore { get; set; }
    public int TieBreakPriority { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class AdminEvaluationApplicationViewModel
{
    public Guid ApplicationPublicId { get; set; }
    public string StudentFullName { get; set; } = string.Empty;
    public string MaskedTc { get; set; } = string.Empty;
    public ApplicationStatus CurrentStatus { get; set; }
    public string DocumentReviewSummary { get; set; } = string.Empty;
    public EvaluationEligibilityStatus EligibilityStatus { get; set; }
    public string? IneligibilityReason { get; set; }
    public decimal? TotalScore { get; set; }
    public int? Rank { get; set; }
    public EvaluationOutcome? Outcome { get; set; }
    public string EvaluationRowVersion { get; set; } = string.Empty;
    public IReadOnlyList<EvaluationComponentViewModel> Components { get; set; } = [];
}

public sealed class EvaluationCapabilitiesViewModel
{
    public bool CanEditPolicy { get; set; }
    public bool CanDecideEligibility { get; set; }
    public bool CanEditManualScore { get; set; }
    public bool CanFinalize { get; set; }
    public bool CanPublish { get; set; }
}

public sealed class AdminEvaluationViewModel
{
    public int ProgramOfferingId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public int AcademicYearStart { get; set; }
    public string AcademicYear { get; set; } = string.Empty;
    public AcademicTerm Term { get; set; }
    public string TermName { get; set; } = string.Empty;
    public DateTime? ApplicationStartUtc { get; set; }
    public DateTime? ApplicationDeadlineUtc { get; set; }
    public int Quota { get; set; }
    public bool IsOpen { get; set; }
    public bool UsesEvaluationWorkflow { get; set; }
    public OfferingEvaluationState EvaluationState { get; set; }
    public DateTime? EvaluationFinalizedAtUtc { get; set; }
    public DateTime? ResultsPublishedAtUtc { get; set; }
    public string OfferingRowVersion { get; set; } = string.Empty;
    public EvaluationCapabilitiesViewModel Capabilities { get; set; } = new();
    public IReadOnlyList<EvaluationCriterionViewModel> Criteria { get; set; } = [];
    public IReadOnlyList<AdminEvaluationApplicationViewModel> Applications { get; set; } = [];
    public IReadOnlyList<ExamRequirementViewModel> EligibleExamRequirements { get; set; } = [];
}

public sealed class EvaluationRankingRowViewModel
{
    public Guid ApplicationPublicId { get; set; }
    public string StudentFullName { get; set; } = string.Empty;
    public string MaskedTc { get; set; } = string.Empty;
    public decimal TotalScore { get; set; }
    public int Rank { get; set; }
    public EvaluationOutcome ProjectedOutcome { get; set; }
    public IReadOnlyList<EvaluationComponentViewModel> Components { get; set; } = [];
}

public sealed class EvaluationRankingPreviewViewModel
{
    public bool CanFinalize { get; set; }
    public IReadOnlyList<string> BlockingReasons { get; set; } = [];
    public IReadOnlyList<EvaluationRankingRowViewModel> Rows { get; set; } = [];
}

public sealed class EvaluationPageViewModel
{
    public AdminEvaluationViewModel Evaluation { get; set; } = new();
    public EvaluationRankingPreviewViewModel Preview { get; set; } = new();
    public EvaluationCriterionFormViewModel CriterionForm { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public sealed class EvaluationPublicationSummaryViewModel
{
    public int Quota { get; set; }
    public int AdmittedCount { get; set; }
    public int NotAdmittedCount { get; set; }
    public int IneligibleCount { get; set; }
    public string OfferingRowVersion { get; set; } = string.Empty;
}

public sealed class EvaluationPublishPageViewModel
{
    public int ProgramOfferingId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public AdminEvaluationViewModel Evaluation { get; set; } = new();
    public EvaluationPublicationSummaryViewModel Summary { get; set; } = new();
}

public sealed class PublishedEvaluationComponentViewModel
{
    public string DisplayName { get; set; } = string.Empty;
    public decimal RawScore { get; set; }
    public decimal NormalizedScore { get; set; }
    public int WeightBasisPoints { get; set; }
    public decimal WeightedScore { get; set; }
}

public sealed class PublishedApplicationEvaluationViewModel
{
    public EvaluationOutcome Outcome { get; set; }
    public decimal? TotalScore { get; set; }
    public int? Rank { get; set; }
    public DateTime ResultsPublishedAtUtc { get; set; }
    public IReadOnlyList<PublishedEvaluationComponentViewModel> Components { get; set; } = [];
}
