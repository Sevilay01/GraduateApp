using System.ComponentModel.DataAnnotations;

namespace GraduateApp.Web.Models;

public sealed class OpenProgramSearchViewModel : IValidatableObject
{
    public const int MaximumSearchLength = 100;

    [StringLength(
        MaximumSearchLength,
        ErrorMessage = "Arama metni en fazla 100 karakter olabilir.")]
    [Display(Name = "Program arama")]
    public string? Search { get; set; }

    [Range(
        2000,
        2200,
        ErrorMessage = "Akademik yıl 2000 ile 2200 arasında olmalıdır.")]
    [Display(Name = "Akademik yıl başlangıcı")]
    public int? AcademicYearStart { get; set; }

    [Display(Name = "Dönem")]
    public AcademicTerm? Term { get; set; }

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Search)
        || AcademicYearStart.HasValue
        || Term.HasValue;

    public static OpenProgramSearchViewModel From(
        string? search,
        int? academicYearStart,
        AcademicTerm? term) => new()
        {
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            AcademicYearStart = academicYearStart,
            Term = term
        };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Term.HasValue && !AcademicTermDisplayExtensions.OfferingValues.Contains(Term.Value))
        {
            yield return new ValidationResult(
                "Geçerli bir akademik dönem seçiniz.",
                [nameof(Term)]);
        }
    }
}

public sealed class ProgramViewModel
{
    public int ProgramOfferingId { get; set; }
    public int ProgramId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string? DegreeType { get; set; }
    public string InstituteName { get; set; } = string.Empty;
    public int AcademicYearStart { get; set; }
    public string AcademicYear { get; set; } = string.Empty;
    public AcademicTerm Term { get; set; }
    public string TermName { get; set; } = string.Empty;
    public DateTime ApplicationStartUtc { get; set; }
    public DateTime ApplicationDeadlineUtc { get; set; }
    public int Quota { get; set; }
    public IReadOnlyList<ExamRequirementViewModel> ExamRequirements { get; set; } = [];
}

public sealed class ExamRequirementViewModel
{
    public int ExamId { get; set; }
    public string ExamName { get; set; } = string.Empty;
    public decimal MinimumScore { get; set; }
    public DateOnly? MinimumValidityDate { get; set; }
    public bool IsRequired { get; set; }
}

public sealed class PanelApplicationViewModel
{
    public Guid PublicId { get; set; }
    public int ProgramOfferingId { get; set; }
    public int ProgramId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public int AcademicYearStart { get; set; }
    public string AcademicYear { get; set; } = string.Empty;
    public AcademicTerm Term { get; set; }
    public string TermName { get; set; } = string.Empty;
    public DateTime ApplicationDateUtc { get; set; }
    public ApplicationStatus CurrentStatus { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public enum DocumentContentCategory
{
    PdfOnly,
    ImageOnly,
    PdfOrImage
}

public enum DocumentReviewStatus
{
    Pending,
    Approved,
    Rejected
}

public sealed class ApplicationDocumentViewModel
{
    public Guid PublicId { get; set; }
    public int VersionNumber { get; set; }
    public bool IsCurrent { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string VerifiedContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DocumentReviewStatus ReviewStatus { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ApplicationDocumentRequirementViewModel
{
    public Guid PublicId { get; set; }
    public string DocumentCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public DocumentContentCategory AllowedContentCategory { get; set; }
    public long MaximumBytes { get; set; }
    public ApplicationDocumentViewModel? CurrentDocument { get; set; }
    public IReadOnlyList<ApplicationDocumentViewModel> Versions { get; set; } = [];
}

public sealed class StudentApplicationDetailViewModel
{
    public Guid PublicId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string InstituteName { get; set; } = string.Empty;
    public int AcademicYearStart { get; set; }
    public string AcademicYear { get; set; } = string.Empty;
    public AcademicTerm Term { get; set; }
    public string TermName { get; set; } = string.Empty;
    public int Quota { get; set; }
    public DateTime ApplicationDateUtc { get; set; }
    public ApplicationStatus CurrentStatus { get; set; }
    public bool UsesDocumentWorkflow { get; set; }
    public bool UsesEvaluationWorkflow { get; set; }
    public PublishedApplicationEvaluationViewModel? PublishedEvaluation { get; set; }
    public IReadOnlyList<ApplicationDocumentRequirementViewModel> DocumentRequirements { get; set; } = [];
    public IReadOnlyList<string> MissingRequiredDocuments { get; set; } = [];
}

public static class DocumentWorkflowSubmissionPresentation
{
    public static bool HasValidRequiredDocumentConfiguration(this StudentApplicationDetailViewModel model) =>
        model.UsesDocumentWorkflow
        && model.DocumentRequirements.Count > 0
        && model.DocumentRequirements.Any(item => item.IsRequired);

    public static bool CanSubmitDocumentWorkflow(this StudentApplicationDetailViewModel model) =>
        model.CurrentStatus == ApplicationStatus.Draft
        && model.HasValidRequiredDocumentConfiguration()
        && model.DocumentRequirements
            .Where(item => item.IsRequired)
            .All(item => item.CurrentDocument is not null);
}

public static class DocumentPresentation
{
    public static string CategoryName(this DocumentContentCategory category) => category switch
    {
        DocumentContentCategory.PdfOnly => "PDF",
        DocumentContentCategory.ImageOnly => "JPEG veya PNG",
        DocumentContentCategory.PdfOrImage => "PDF, JPEG veya PNG",
        _ => "Bilinmiyor"
    };

    public static string ReviewName(this DocumentReviewStatus status) => status switch
    {
        DocumentReviewStatus.Pending => "İnceleme bekliyor",
        DocumentReviewStatus.Approved => "Onaylandı",
        DocumentReviewStatus.Rejected => "Reddedildi",
        _ => "Bilinmiyor"
    };

    public static string FileSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024d * 1024d):0.##} MB"
        : $"{bytes / 1024d:0.##} KB";
}

public sealed class PanelDashboardViewModel
{
    public OpenProgramSearchViewModel ProgramSearch { get; set; } = new();
    public IReadOnlyList<ProgramViewModel> OpenPrograms { get; set; } = [];
    public IReadOnlyList<PanelApplicationViewModel> Applications { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

public sealed class HomeViewModel
{
    public OpenProgramSearchViewModel ProgramSearch { get; set; } = new();
    public IReadOnlyList<ProgramViewModel> OpenPrograms { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

public sealed class StudentExamScoreViewModel
{
    public int ScoreId { get; set; }
    public int ExamId { get; set; }
    public string ExamName { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public DateOnly? ExamDate { get; set; }
}

public sealed class StudentExamCatalogItemViewModel
{
    public int ExamId { get; set; }
    public string ExamName { get; set; } = string.Empty;
    public decimal MaximumScore { get; set; }
}

public sealed class StudentExamScoreInputViewModel
{
    public int ScoreId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir sınav seçiniz.")]
    [Display(Name = "Sınav")]
    public int ExamId { get; set; }

    [Required(ErrorMessage = "Sınav puanı zorunludur.")]
    [Range(
        typeof(decimal),
        "0",
        "999.99",
        ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Sınav puanı 0 ile 999,99 arasında olmalıdır.")]
    [Display(Name = "Puan")]
    public decimal? Score { get; set; }

    [Required(ErrorMessage = "Sınav tarihi zorunludur.")]
    [Display(Name = "Sınav tarihi")]
    public DateOnly? ExamDate { get; set; }
}

public sealed class StudentExamScoresPageViewModel
{
    public IReadOnlyList<StudentExamScoreViewModel> Scores { get; set; } = [];
    public IReadOnlyList<StudentExamCatalogItemViewModel> Exams { get; set; } = [];
    public StudentExamScoreInputViewModel Form { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
