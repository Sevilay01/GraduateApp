using GraduateApp.API.Domain;

namespace GraduateApp.API.DTOs;

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
    IReadOnlyList<ExamRequirementDto> ExamRequirements);

public sealed record ExamRequirementDto(
    int ExamId,
    string ExamName,
    decimal MinimumScore,
    DateOnly? MinimumValidityDate,
    bool IsRequired);

public sealed record StudentApplicationDto(
    int ApplicationId,
    int ProgramOfferingId,
    int ProgramId,
    string ProgramName,
    int AcademicYearStart,
    string AcademicYear,
    AcademicTerm Term,
    string TermName,
    DateTime ApplicationDateUtc,
    ApplicationStatus CurrentStatus,
    string RowVersion);

public sealed record AdminApplicationListItemDto(
    int ApplicationId,
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
    int ApplicationId,
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
    IReadOnlyList<ApplicationScoreSnapshotDto> ScoreSnapshots);

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
