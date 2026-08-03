using System.ComponentModel.DataAnnotations;
using GraduateApp.API.Domain;

namespace GraduateApp.API.DTOs;

public sealed class OpenProgramSearchQueryDto
{
    public const int MaximumSearchLength = 100;

    [StringLength(
        MaximumSearchLength,
        ErrorMessage = "Arama metni en fazla 100 karakter olabilir.")]
    public string? Search { get; init; }

    [Range(
        2000,
        2200,
        ErrorMessage = "Akademik yıl 2000 ile 2200 arasında olmalıdır.")]
    public int? AcademicYearStart { get; init; }

    public AcademicTerm? Term { get; init; }
}

public sealed record OpenProgramDto(
    int ProgramOfferingId,
    int ProgramId,
    string ProgramName,
    string? DegreeType,
    string InstituteName,
    int AcademicYearStart,
    string AcademicYear,
    AcademicTerm Term,
    string TermName,
    DateTime ApplicationStartUtc,
    DateTime ApplicationDeadlineUtc,
    int Quota,
    IReadOnlyList<ExamRequirementDto> ExamRequirements)
{
    public string? ProgramNameEnglish { get; init; }
}

public sealed record ExamRequirementDto(
    int ExamId,
    string ExamName,
    decimal MinimumScore,
    DateOnly? MinimumValidityDate,
    bool IsRequired);

public sealed record StudentApplicationDto(
    Guid PublicId,
    int ProgramOfferingId,
    int ProgramId,
    string ProgramName,
    int AcademicYearStart,
    string AcademicYear,
    AcademicTerm Term,
    string TermName,
    DateTime ApplicationDateUtc,
    ApplicationStatus CurrentStatus,
    string RowVersion)
{
    public string? ProgramNameEnglish { get; init; }
}

public sealed record AdminApplicationListItemDto(
    Guid PublicId,
    string StudentFullName,
    string MaskedTc,
    string ProgramName,
    int AcademicYearStart,
    string AcademicYear,
    AcademicTerm Term,
    string TermName,
    DateTime ApplicationDateUtc,
    ApplicationStatus CurrentStatus,
    string RowVersion);

public sealed record ApplicationStatusHistoryDto(
    ApplicationStatus? PreviousStatus,
    ApplicationStatus CurrentStatus,
    DateTime ChangedAtUtc,
    string? Notes);

public sealed record AdminApplicationDetailDto(
    Guid PublicId,
    string MaskedTc,
    string StudentFullName,
    string Email,
    string ProgramName,
    string InstituteName,
    int AcademicYearStart,
    string AcademicYear,
    AcademicTerm Term,
    string TermName,
    DateTime ApplicationDateUtc,
    ApplicationStatus CurrentStatus,
    string RowVersion,
    IReadOnlyList<ApplicationStatusHistoryDto> History,
    IReadOnlyList<ApplicationScoreSnapshotDto> ScoreSnapshots,
    bool UsesDocumentWorkflow,
    bool UsesEvaluationWorkflow,
    IReadOnlyList<ApplicationDocumentRequirementDto> DocumentRequirements);

public sealed record ApplicationScoreSnapshotDto(
    int ExamId,
    string ExamName,
    decimal Score,
    DateOnly? ExamDate,
    DateTime CapturedAtUtc);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
