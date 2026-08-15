using System.ComponentModel.DataAnnotations;
using GraduateApp.API.Domain;

namespace GraduateApp.API.DTOs;

public class EvaluationCriterionCreateDto
{
    [Required(ErrorMessage = "Kriter kodu zorunludur.")]
    [StringLength(64, MinimumLength = 2, ErrorMessage = "Kriter kodu 2 ile 64 karakter arasında olmalıdır.")]
    public string Code { get; init; } = string.Empty;

    [Required(ErrorMessage = "Kriter adı zorunludur.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Kriter adı 2 ile 150 karakter arasında olmalıdır.")]
    public string DisplayName { get; init; } = string.Empty;

    [EnumDataType(typeof(EvaluationCriterionSourceType), ErrorMessage = "Geçersiz kriter kaynak türü.")]
    public EvaluationCriterionSourceType SourceType { get; init; }

    public int? ExamId { get; init; }

    [Range(1, EvaluationScoring.TotalWeightBasisPoints, ErrorMessage = "Kriter ağırlığı 1 ile 10000 basis point arasında olmalıdır.")]
    public int WeightBasisPoints { get; init; }

    [Range(typeof(decimal), "0.0001", "99999", ParseLimitsInInvariantCulture = true, ErrorMessage = "Maksimum ham puan geçersiz.")]
    public decimal MaximumRawScore { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Eşitlik bozma önceliği pozitif olmalıdır.")]
    public int TieBreakPriority { get; init; }
}

public sealed class EvaluationCriterionUpdateDto : EvaluationCriterionCreateDto
{
    [Required(ErrorMessage = "Kriter eşzamanlılık bilgisi zorunludur.")]
    [StringLength(64, ErrorMessage = "Kriter eşzamanlılık bilgisi geçersiz.")]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class EvaluationCriterionDeleteDto
{
    [Required(ErrorMessage = "Kriter eşzamanlılık bilgisi zorunludur.")]
    [StringLength(64, ErrorMessage = "Kriter eşzamanlılık bilgisi geçersiz.")]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record ProgramOfferingEvaluationCriterionDto(
    Guid PublicId,
    string Code,
    string DisplayName,
    EvaluationCriterionSourceType SourceType,
    int? ExamId,
    string? ExamName,
    int WeightBasisPoints,
    decimal MaximumRawScore,
    int TieBreakPriority,
    string RowVersion);

public sealed class EligibilityDecisionDto
{
    [EnumDataType(typeof(EvaluationEligibilityStatus), ErrorMessage = "Geçersiz uygunluk kararı.")]
    public EvaluationEligibilityStatus EligibilityStatus { get; init; }

    [StringLength(500, ErrorMessage = "Uygun olmama gerekçesi en fazla 500 karakter olabilir.")]
    public string? IneligibilityReason { get; init; }

    [Required(ErrorMessage = "Değerlendirme eşzamanlılık bilgisi zorunludur.")]
    [StringLength(64, ErrorMessage = "Değerlendirme eşzamanlılık bilgisi geçersiz.")]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class ManualEvaluationScoreDto
{
    [Required(ErrorMessage = "Manuel puan zorunludur.")]
    [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true, ErrorMessage = "Manuel puan 0 ile 100 arasında olmalıdır.")]
    public decimal? RawScore { get; init; }

    [Required(ErrorMessage = "Puan bileşeni eşzamanlılık bilgisi zorunludur.")]
    [StringLength(64, ErrorMessage = "Puan bileşeni eşzamanlılık bilgisi geçersiz.")]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class OfferingEvaluationCommandDto
{
    [Required(ErrorMessage = "İlan eşzamanlılık bilgisi zorunludur.")]
    [StringLength(64, ErrorMessage = "İlan eşzamanlılık bilgisi geçersiz.")]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record EvaluationComponentDto(
    Guid CriterionPublicId,
    string Code,
    string DisplayName,
    EvaluationCriterionSourceType SourceType,
    decimal? RawScore,
    decimal MaximumRawScore,
    decimal? NormalizedScore,
    int WeightBasisPoints,
    decimal? WeightedScore,
    int TieBreakPriority,
    string RowVersion);

public sealed record AdminEvaluationApplicationDto(
    Guid ApplicationPublicId,
    string StudentFullName,
    string MaskedTc,
    ApplicationStatus CurrentStatus,
    string DocumentReviewSummary,
    EvaluationEligibilityStatus EligibilityStatus,
    string? IneligibilityReason,
    decimal? TotalScore,
    int? Rank,
    EvaluationOutcome? Outcome,
    string EvaluationRowVersion,
    IReadOnlyList<EvaluationComponentDto> Components);

public sealed record EvaluationCapabilitiesDto(
    bool CanEditPolicy,
    bool CanDecideEligibility,
    bool CanEditManualScore,
    bool CanFinalize,
    bool CanPublish);

public sealed record AdminEvaluationPageDto(
    int ProgramOfferingId,
    string ProgramName,
    string? ProgramNameEnglish,
    string DegreeType,
    int AcademicYearStart,
    string AcademicYear,
    AcademicTerm Term,
    string TermName,
    DateTime? ApplicationStartUtc,
    DateTime? ApplicationDeadlineUtc,
    int Quota,
    bool IsOpen,
    bool UsesEvaluationWorkflow,
    OfferingEvaluationState EvaluationState,
    DateTime? EvaluationFinalizedAtUtc,
    DateTime? ResultsPublishedAtUtc,
    string OfferingRowVersion,
    EvaluationCapabilitiesDto Capabilities,
    IReadOnlyList<ProgramOfferingEvaluationCriterionDto> Criteria,
    IReadOnlyList<AdminEvaluationApplicationDto> Applications,
    IReadOnlyList<ExamRequirementDto> EligibleExamRequirements);

public sealed record EvaluationRankingRowDto(
    Guid ApplicationPublicId,
    string StudentFullName,
    string MaskedTc,
    decimal TotalScore,
    int Rank,
    EvaluationOutcome ProjectedOutcome,
    IReadOnlyList<EvaluationComponentDto> Components);

public sealed record EvaluationRankingPreviewDto(
    bool CanFinalize,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<EvaluationRankingRowDto> Rows);

public sealed record EvaluationPublicationSummaryDto(
    int Quota,
    int AdmittedCount,
    int NotAdmittedCount,
    int IneligibleCount,
    string OfferingRowVersion);

public sealed record PublishedEvaluationComponentDto(
    string DisplayName,
    decimal RawScore,
    decimal NormalizedScore,
    int WeightBasisPoints,
    decimal WeightedScore);

public sealed record PublishedApplicationEvaluationDto(
    EvaluationOutcome Outcome,
    decimal? TotalScore,
    int? Rank,
    DateTime ResultsPublishedAtUtc,
    IReadOnlyList<PublishedEvaluationComponentDto> Components);
