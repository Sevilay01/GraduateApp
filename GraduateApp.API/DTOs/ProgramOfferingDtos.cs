using System.ComponentModel.DataAnnotations;
using GraduateApp.API.Domain;

namespace GraduateApp.API.DTOs;

public sealed class ProgramOfferingRequirementInputDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir sınav seçiniz.")]
    public int ExamId { get; init; }

    [Range(
        typeof(decimal),
        "0",
        "999.99",
        ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Puan 0 ile 999,99 arasında olmalıdır.")]
    public decimal MinimumScore { get; init; }

    public DateOnly? MinimumValidityDate { get; init; }
    public bool IsRequired { get; init; }
}

public class ProgramOfferingCreateDto : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir program seçiniz.")]
    public int ProgramId { get; init; }

    [Range(2000, 2200, ErrorMessage = "Akademik yıl başlangıcı 2000 ile 2200 arasında olmalıdır.")]
    public int AcademicYearStart { get; init; }

    [EnumDataType(typeof(AcademicTerm), ErrorMessage = "Geçerli bir dönem seçiniz.")]
    public AcademicTerm Term { get; init; }

    public DateTime ApplicationStartUtc { get; init; }
    public DateTime ApplicationDeadlineUtc { get; init; }

    [Range(1, 100000, ErrorMessage = "Kontenjan 1 ile 100000 arasında olmalıdır.")]
    public int Quota { get; init; }

    public bool IsOpen { get; init; }
    public IReadOnlyList<ProgramOfferingRequirementInputDto> ExamRequirements { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Term == AcademicTerm.LegacyUnspecified)
        {
            yield return new ValidationResult("Yeni ilanlar için geçerli bir dönem seçiniz.", [nameof(Term)]);
        }

        if (ApplicationStartUtc >= ApplicationDeadlineUtc)
        {
            yield return new ValidationResult(
                "Başvuru başlangıcı son başvuru tarihinden önce olmalıdır.",
                [nameof(ApplicationStartUtc), nameof(ApplicationDeadlineUtc)]);
        }

        if (ExamRequirements.GroupBy(item => item.ExamId).Any(group => group.Count() > 1))
        {
            yield return new ValidationResult("Aynı sınav koşulu birden fazla kez eklenemez.", [nameof(ExamRequirements)]);
        }
    }
}

public sealed class ProgramOfferingUpdateDto : ProgramOfferingCreateDto
{
    [Required(ErrorMessage = "İlan eşzamanlılık bilgisi zorunludur.")]
    [StringLength(
        64,
        ErrorMessage = "İlan eşzamanlılık bilgisi geçersiz.")]
    public string RowVersion { get; init; } = string.Empty;
    public bool IsArchived { get; init; }
}

public sealed record ProgramOfferingAdminDto(
    int ProgramOfferingId,
    int ProgramId,
    string ProgramName,
    int AcademicYearStart,
    string AcademicYear,
    AcademicTerm Term,
    string TermName,
    DateTime? ApplicationStartUtc,
    DateTime? ApplicationDeadlineUtc,
    int Quota,
    bool IsOpen,
    bool IsArchived,
    string RowVersion,
    IReadOnlyList<ExamRequirementDto> ExamRequirements);

public sealed record ProgramCatalogItemDto(
    int ProgramId,
    string ProgramName,
    string InstituteName,
    string DegreeType);
public sealed record ExamCatalogItemDto(int ExamId, string ExamName);
public sealed record ProgramOfferingCatalogDto(
    IReadOnlyList<ProgramCatalogItemDto> Programs,
    IReadOnlyList<ExamCatalogItemDto> Exams);
