using System.ComponentModel.DataAnnotations;
using GraduateApp.API.Domain;

namespace GraduateApp.API.DTOs;

public class OfferingDocumentRequirementCreateDto : IValidatableObject
{
    [Required(ErrorMessage = "Belge kodu zorunludur.")]
    [StringLength(64, MinimumLength = 2, ErrorMessage = "Belge kodu 2 ile 64 karakter arasında olmalıdır.")]
    public string DocumentCode { get; init; } = string.Empty;

    [Required(ErrorMessage = "Türkçe görünen ad zorunludur.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Görünen ad 2 ile 150 karakter arasında olmalıdır.")]
    public string DisplayName { get; init; } = string.Empty;

    [StringLength(500, ErrorMessage = "Açıklama en fazla 500 karakter olabilir.")]
    public string? Description { get; init; }

    public bool IsRequired { get; init; }

    [EnumDataType(typeof(DocumentContentCategory), ErrorMessage = "Geçersiz içerik kategorisi.")]
    public DocumentContentCategory AllowedContentCategory { get; init; }

    [Range(1, DocumentWorkflowCatalog.AbsoluteMaximumUploadBytes, ErrorMessage = "Belge boyutu sınırı geçersiz.")]
    public long MaximumBytes { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var code = DocumentCode.Trim();
        if (code.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            yield return new ValidationResult(
                "Belge kodunda yalnızca İngilizce harf, rakam, tire ve alt çizgi kullanılabilir.",
                [nameof(DocumentCode)]);
        }
    }
}

public sealed class OfferingDocumentRequirementUpdateDto : OfferingDocumentRequirementCreateDto
{
    [Required(ErrorMessage = "Eşzamanlılık bilgisi zorunludur.")]
    [StringLength(64, ErrorMessage = "Eşzamanlılık bilgisi geçersiz.")]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class DocumentRequirementActiveDto
{
    public bool IsActive { get; init; }

    [Required(ErrorMessage = "Eşzamanlılık bilgisi zorunludur.")]
    [StringLength(64, ErrorMessage = "Eşzamanlılık bilgisi geçersiz.")]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed record OfferingDocumentRequirementDto(
    Guid PublicId,
    string DocumentCode,
    string DisplayName,
    string? Description,
    bool IsRequired,
    bool IsActive,
    DocumentContentCategory AllowedContentCategory,
    long MaximumBytes,
    string RowVersion);

public sealed record ApplicationDocumentDto(
    Guid PublicId,
    int VersionNumber,
    bool IsCurrent,
    string OriginalFileName,
    string VerifiedContentType,
    long FileSize,
    DocumentReviewStatus ReviewStatus,
    string? RejectionReason,
    DateTime UploadedAtUtc,
    DateTime? ReviewedAtUtc,
    string RowVersion);

public sealed record ApplicationDocumentRequirementDto(
    Guid PublicId,
    string DocumentCode,
    string DisplayName,
    string? Description,
    bool IsRequired,
    DocumentContentCategory AllowedContentCategory,
    long MaximumBytes,
    ApplicationDocumentDto? CurrentDocument,
    IReadOnlyList<ApplicationDocumentDto> Versions);

public sealed record StudentApplicationDetailDto(
    Guid PublicId,
    string ProgramName,
    string InstituteName,
    int AcademicYearStart,
    string AcademicYear,
    AcademicTerm Term,
    string TermName,
    DateTime ApplicationDateUtc,
    ApplicationStatus CurrentStatus,
    bool UsesDocumentWorkflow,
    IReadOnlyList<ApplicationDocumentRequirementDto> DocumentRequirements,
    IReadOnlyList<string> MissingRequiredDocuments);

public sealed record DocumentWorkflowInvariantViolationDto(
    Guid ApplicationPublicId,
    ApplicationStatus CurrentStatus,
    string ViolationCategory);

public sealed class DocumentReviewDto
{
    [EnumDataType(typeof(DocumentReviewStatus), ErrorMessage = "Geçersiz belge inceleme durumu.")]
    public DocumentReviewStatus ReviewStatus { get; init; }

    [StringLength(500, ErrorMessage = "Ret gerekçesi en fazla 500 karakter olabilir.")]
    public string? RejectionReason { get; init; }

    [Required(ErrorMessage = "Eşzamanlılık bilgisi zorunludur.")]
    [StringLength(64, ErrorMessage = "Eşzamanlılık bilgisi geçersiz.")]
    public string RowVersion { get; init; } = string.Empty;
}
