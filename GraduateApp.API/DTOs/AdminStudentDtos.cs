namespace GraduateApp.API.DTOs;

public sealed record AdminStudentListItemDto(
    Guid PublicId,
    string MaskedTc,
    string FullName,
    string Email,
    bool IsActive,
    DateTime UpdatedAtUtc);

public sealed record AdminStudentDetailDto(
    Guid PublicId,
    string MaskedTc,
    string FullName,
    string Email,
    string? Telephone,
    bool IsActive,
    int ApplicationCount,
    int ExamScoreCount,
    DateTime UpdatedAtUtc);
