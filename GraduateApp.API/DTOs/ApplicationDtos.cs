using GraduateApp.API.Domain;

namespace GraduateApp.API.DTOs;

public sealed record OpenProgramDto(
    int ProgramId,
    string ProgramName,
    string? DegreeType,
    string InstituteName,
    DateTime? ApplicationDeadlineUtc);

public sealed record StudentApplicationDto(
    int ApplicationId,
    int ProgramId,
    string ProgramName,
    DateTime ApplicationDateUtc,
    ApplicationStatus CurrentStatus,
    string RowVersion);

public sealed record AdminApplicationListItemDto(
    int ApplicationId,
    string StudentFullName,
    string MaskedTc,
    string ProgramName,
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
    string Tc,
    string StudentFullName,
    string Email,
    string ProgramName,
    string InstituteName,
    DateTime ApplicationDateUtc,
    ApplicationStatus CurrentStatus,
    string RowVersion,
    IReadOnlyList<ApplicationStatusHistoryDto> History);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
