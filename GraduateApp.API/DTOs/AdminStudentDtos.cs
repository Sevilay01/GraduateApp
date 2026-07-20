namespace GraduateApp.API.DTOs;

public sealed record AdminStudentListItemDto(
    string Tc,
    string MaskedTc,
    string FullName,
    string Email,
    bool IsActive,
    DateTime UpdatedAtUtc);

public sealed record AdminStudentDetailDto(
    string Tc,
    string MaskedTc,
    string FullName,
    string Email,
    string? Telephone,
    bool IsActive,
    int ApplicationCount,
    int ExamScoreCount,
    DateTime UpdatedAtUtc);
