using System.ComponentModel.DataAnnotations;

namespace GraduateApp.API.DTOs;

public class InstituteCreateDto
{
    [Required(ErrorMessage = "Enstitü adı zorunludur.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Enstitü adı 2 ile 100 karakter arasında olmalıdır.")]
    public string InstituteName { get; init; } = string.Empty;
}

public sealed class InstituteUpdateDto : InstituteCreateDto
{
    [Required(ErrorMessage = "Enstitü eşzamanlılık bilgisi zorunludur.")]
    [StringLength(24, MinimumLength = 12, ErrorMessage = "Enstitü eşzamanlılık bilgisi geçersiz.")]
    public string RowVersion { get; init; } = string.Empty;
}

public class ProgramCreateDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir enstitü seçiniz.")]
    public int InstituteId { get; init; }

    [Required(ErrorMessage = "Program adı zorunludur.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Program adı 2 ile 100 karakter arasında olmalıdır.")]
    public string ProgramName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Derece türü zorunludur.")]
    [StringLength(50, ErrorMessage = "Derece türü en fazla 50 karakter olabilir.")]
    public string DegreeType { get; init; } = string.Empty;
}

public sealed class ProgramUpdateDto : ProgramCreateDto
{
    [Required(ErrorMessage = "Program eşzamanlılık bilgisi zorunludur.")]
    [StringLength(24, MinimumLength = 12, ErrorMessage = "Program eşzamanlılık bilgisi geçersiz.")]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class CatalogConcurrencyDto
{
    [Required(ErrorMessage = "Eşzamanlılık bilgisi zorunludur.")]
    [StringLength(24, MinimumLength = 12, ErrorMessage = "Eşzamanlılık bilgisi geçersiz.")]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record InstituteAdminDto(
    int InstituteId,
    string InstituteName,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string RowVersion,
    int ProgramCount);

public sealed record ProgramAdminDto(
    int ProgramId,
    int InstituteId,
    string InstituteName,
    string ProgramName,
    string DegreeType,
    bool IsActive,
    bool IsEffectivelyActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string RowVersion,
    int OfferingCount);
