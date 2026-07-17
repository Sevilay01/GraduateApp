using System.ComponentModel.DataAnnotations;

namespace GraduateApp.API.DTOs;

public sealed class UpdateStudentProfileDto
{
    [Required, StringLength(50, MinimumLength = 2)]
    public string FirstName { get; init; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    public string LastName { get; init; } = string.Empty;

    [EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Phone, StringLength(15)]
    public string? Telephone { get; init; }

    [StringLength(50)]
    public string? FatherName { get; init; }

    public DateOnly? BirthDate { get; init; }

    public EducationDto? Education { get; init; }
}

public sealed class EducationDto
{
    [Range(1, int.MaxValue)]
    public int UniversityId { get; init; }

    [StringLength(100)]
    public string? Faculty { get; init; }

    [StringLength(100)]
    public string? GraduatedProgram { get; init; }

    [Range(0, 4)]
    public decimal? Gno { get; init; }
}

public sealed record StudentProfileDto(
    string TcMasked,
    string FirstName,
    string LastName,
    string Email,
    string? Telephone,
    string? FatherName,
    DateOnly? BirthDate,
    EducationDto? Education);

public sealed record UniversityDto(int UniversityId, string UniversityName);
