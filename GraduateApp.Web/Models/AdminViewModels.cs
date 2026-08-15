using System.ComponentModel.DataAnnotations;

namespace GraduateApp.Web.Models;

public sealed class AdminApplicationListItemViewModel
{
    public Guid PublicId { get; set; }
    public string StudentFullName { get; set; } = string.Empty;
    public string MaskedTc { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public int AcademicYearStart { get; set; }
    public string AcademicYear { get; set; } = string.Empty;
    public AcademicTerm Term { get; set; }
    public string TermName { get; set; } = string.Empty;
    public DateTime ApplicationDateUtc { get; set; }
    public ApplicationStatus CurrentStatus { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class PagedResultViewModel<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public sealed class AdminApplicationListViewModel
{
    public PagedResultViewModel<AdminApplicationListItemViewModel> Result { get; set; } = new();
    public string? Search { get; set; }
    public ApplicationStatus? Status { get; set; }
    public int? AcademicYearStart { get; set; }
    public AcademicTerm? Term { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class ApplicationStatusHistoryViewModel
{
    public ApplicationStatus? PreviousStatus { get; set; }
    public ApplicationStatus CurrentStatus { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string? Notes { get; set; }
}

public sealed class AdminApplicationDetailViewModel
{
    public Guid PublicId { get; set; }
    public string MaskedTc { get; set; } = string.Empty;
    public string StudentFullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public string InstituteName { get; set; } = string.Empty;
    public int AcademicYearStart { get; set; }
    public string AcademicYear { get; set; } = string.Empty;
    public AcademicTerm Term { get; set; }
    public string TermName { get; set; } = string.Empty;
    public DateTime ApplicationDateUtc { get; set; }
    public ApplicationStatus CurrentStatus { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<ApplicationStatusHistoryViewModel> History { get; set; } = [];
    public IReadOnlyList<ApplicationScoreSnapshotViewModel> ScoreSnapshots { get; set; } = [];
    public bool UsesDocumentWorkflow { get; set; }
    public bool UsesEvaluationWorkflow { get; set; }
    public IReadOnlyList<ApplicationDocumentRequirementViewModel> DocumentRequirements { get; set; } = [];
}

public sealed class ApplicationScoreSnapshotViewModel
{
    public int ExamId { get; set; }
    public string ExamName { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public DateOnly? ExamDate { get; set; }
    public DateTime CapturedAtUtc { get; set; }
}

public sealed class ProgramOfferingAdminViewModel
{
    public int ProgramOfferingId { get; set; }
    public int ProgramId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string? ProgramNameEnglish { get; set; }
    public string InstituteName { get; set; } = string.Empty;
    public string DegreeType { get; set; } = string.Empty;
    public int AcademicYearStart { get; set; }
    public string AcademicYear { get; set; } = string.Empty;
    public AcademicTerm Term { get; set; }
    public string TermName { get; set; } = string.Empty;
    public DateTime? ApplicationStartUtc { get; set; }
    public DateTime? ApplicationDeadlineUtc { get; set; }
    public int Quota { get; set; }
    public bool IsOpen { get; set; }
    public bool IsArchived { get; set; }
    public bool UsesDocumentWorkflow { get; set; }
    public bool UsesEvaluationWorkflow { get; set; }
    public OfferingEvaluationState EvaluationState { get; set; }
    public DateTime? EvaluationFinalizedAtUtc { get; set; }
    public DateTime? ResultsPublishedAtUtc { get; set; }
    public int DocumentRequirementCount { get; set; }
    public int ActiveRequiredDocumentRequirementCount { get; set; }
    public bool HasActiveRequiredDocumentRequirement { get; set; }
    public int DraftApplicationCount { get; set; }
    public int SubmittedOrLaterApplicationCount { get; set; }
    public OfferingDocumentConfigurationHealth DocumentConfigurationHealth { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<ExamRequirementViewModel> ExamRequirements { get; set; } = [];
}

public enum OfferingDocumentConfigurationHealth
{
    LegacyOutsideDocumentWorkflow,
    OpenHealthy,
    OpenInvalidNoApplications,
    OpenInvalidWithDrafts,
    OpenInvalidWithSubmittedApplications,
    ClosedWorkflow
}

public sealed class ProgramCatalogItemViewModel
{
    public int ProgramId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string InstituteName { get; set; } = string.Empty;
    public string DegreeType { get; set; } = string.Empty;
}

public sealed class ExamCatalogItemViewModel
{
    public int ExamId { get; set; }
    public string ExamName { get; set; } = string.Empty;
    public bool IsAles { get; set; }
}

public sealed class ProgramOfferingCatalogViewModel
{
    public IReadOnlyList<ProgramCatalogItemViewModel> Programs { get; set; } = [];
    public IReadOnlyList<ExamCatalogItemViewModel> Exams { get; set; } = [];
}

public sealed class ProgramOfferingRequirementInputViewModel
{
    public bool IsConfigured { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Validation.ValidExam")]
    public int ExamId { get; set; }

    [Range(
        typeof(decimal),
        "0",
        "999.99",
        ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Validation.ScoreRange")]
    public decimal MinimumScore { get; set; }
    public DateOnly? MinimumValidityDate { get; set; }
    public bool IsRequired { get; set; }
}

public sealed class ProgramOfferingFormViewModel
{
    public int ProgramOfferingId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Validation.ValidProgram")]
    [Display(Name = "Program")]
    public int ProgramId { get; set; }

    [Range(2000, 2200, ErrorMessage = "Validation.AcademicYearRange")]
    [Display(Name = "Field.AcademicYearStart")]
    public int AcademicYearStart { get; set; }

    [EnumDataType(typeof(AcademicTerm), ErrorMessage = "Validation.ValidTerm")]
    [Display(Name = "Field.Term")]
    public AcademicTerm Term { get; set; }

    [Required(ErrorMessage = "Validation.StartRequired")]
    [DataType(DataType.DateTime)]
    [Display(Name = "Field.ApplicationStartIstanbul")]
    public DateTime ApplicationStartLocal { get; set; }

    [Required(ErrorMessage = "Validation.DeadlineRequired")]
    [DataType(DataType.DateTime)]
    [Display(Name = "Field.ApplicationDeadlineIstanbul")]
    public DateTime ApplicationDeadlineLocal { get; set; }

    [Range(1, 100000, ErrorMessage = "Validation.QuotaRange")]
    [Display(Name = "Kontenjan")]
    public int Quota { get; set; }

    [Display(Name = "Field.OfferingOpen")]
    public bool IsOpen { get; set; }

    [Display(Name = "Field.Archive")]
    public bool IsArchived { get; set; }

    [Display(Name = "Field.RankedWorkflow")]
    public bool UsesEvaluationWorkflow { get; set; } = true;

    [StringLength(
        64,
        ErrorMessage = "Validation.OfferingConcurrency")]
    public string? RowVersion { get; set; }
    public List<ProgramOfferingRequirementInputViewModel> ExamRequirements { get; set; } = [];
    public IReadOnlyList<ProgramCatalogItemViewModel> Programs { get; set; } = [];
    public IReadOnlyList<ExamCatalogItemViewModel> Exams { get; set; } = [];
}

public enum OfferingSection
{
    Overview,
    Edit,
    ExamRequirements,
    DocumentRequirements,
    Applications,
    EvaluationCriteria,
    Evaluation,
    Results
}

public sealed class OfferingHeaderViewModel
{
    public int ProgramOfferingId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string? ProgramNameEnglish { get; set; }
    public string DegreeType { get; set; } = string.Empty;
    public string AcademicYear { get; set; } = string.Empty;
    public AcademicTerm Term { get; set; }
    public string TermName { get; set; } = string.Empty;
    public bool IsOpen { get; set; }
    public bool IsArchived { get; set; }
    public bool UsesDocumentWorkflow { get; set; }
    public bool UsesEvaluationWorkflow { get; set; }
    public OfferingEvaluationState EvaluationState { get; set; }
}

public sealed class OfferingShellViewModel
{
    public OfferingHeaderViewModel Header { get; set; } = new();
    public OfferingSection ActiveSection { get; set; }
}

public class ProgramOfferingListPageViewModel
{
    public IReadOnlyList<ProgramOfferingAdminViewModel> Offerings { get; set; } = [];
    public int? AcademicYearStart { get; set; }
    public AcademicTerm? Term { get; set; }
    public bool IncludeArchived { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class OfferingOverviewPageViewModel
{
    public OfferingShellViewModel Shell { get; set; } = new();
    public DateTime? ApplicationStartUtc { get; set; }
    public DateTime? ApplicationDeadlineUtc { get; set; }
    public int Quota { get; set; }
    public int DocumentRequirementCount { get; set; }
    public int ActiveRequiredDocumentRequirementCount { get; set; }
    public int DraftApplicationCount { get; set; }
    public int SubmittedOrLaterApplicationCount { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public sealed class OfferingEditFormViewModel
{
    public int ProgramOfferingId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Validation.ValidProgram")]
    public int ProgramId { get; set; }

    [Range(2000, 2200, ErrorMessage = "Validation.AcademicYearRange")]
    public int AcademicYearStart { get; set; }

    [EnumDataType(typeof(AcademicTerm), ErrorMessage = "Validation.ValidTerm")]
    public AcademicTerm Term { get; set; }

    [Required(ErrorMessage = "Validation.StartRequired")]
    [DataType(DataType.DateTime)]
    public DateTime ApplicationStartLocal { get; set; }

    [Required(ErrorMessage = "Validation.DeadlineRequired")]
    [DataType(DataType.DateTime)]
    public DateTime ApplicationDeadlineLocal { get; set; }

    [Range(1, 100000, ErrorMessage = "Validation.QuotaRange")]
    public int Quota { get; set; }
    public bool IsOpen { get; set; }
    public bool IsArchived { get; set; }
    public bool UsesEvaluationWorkflow { get; set; } = true;

    [StringLength(64, ErrorMessage = "Validation.OfferingConcurrency")]
    public string? RowVersion { get; set; }
}

public sealed class OfferingEditPageViewModel
{
    public OfferingShellViewModel? Shell { get; set; }
    public OfferingEditFormViewModel Form { get; set; } = new();
    public IReadOnlyList<ProgramCatalogItemViewModel> Programs { get; set; } = [];
    public bool HasActiveRequiredDocumentRequirement { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class OfferingExamRequirementsFormViewModel
{
    public int ProgramOfferingId { get; set; }

    [Required(ErrorMessage = "Validation.OfferingConcurrency")]
    [StringLength(64, ErrorMessage = "Validation.OfferingConcurrency")]
    public string RowVersion { get; set; } = string.Empty;
    public List<ProgramOfferingRequirementInputViewModel> Requirements { get; set; } = [];
}

public sealed class OfferingExamRequirementsPageViewModel
{
    public OfferingShellViewModel Shell { get; set; } = new();
    public OfferingExamRequirementsFormViewModel Form { get; set; } = new();
    public IReadOnlyList<ExamCatalogItemViewModel> Exams { get; set; } = [];
    public bool CanEdit { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class OfferingDocumentRequirementsPageViewModel
{
    public OfferingShellViewModel Shell { get; set; } = new();
    public IReadOnlyList<OfferingDocumentRequirementViewModel> Requirements { get; set; } = [];
    public OfferingDocumentRequirementFormViewModel DocumentRequirementForm { get; set; } = new();
    public bool CanEdit { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class OfferingApplicationsPageViewModel
{
    public OfferingShellViewModel Shell { get; set; } = new();
    public PagedResultViewModel<AdminApplicationListItemViewModel> Result { get; set; } = new();
    public string? Search { get; set; }
    public ApplicationStatus? Status { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class UpdateApplicationStatusViewModel
{
    public Guid PublicId { get; set; }

    [EnumDataType(typeof(ApplicationStatus))]
    [Display(Name = "Yeni durum")]
    public ApplicationStatus NewStatus { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Not")]
    public string? Notes { get; set; }
}

public sealed class OfferingDocumentRequirementViewModel
{
    public Guid PublicId { get; set; }
    public string DocumentCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; }
    public DocumentContentCategory AllowedContentCategory { get; set; }
    public long MaximumBytes { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class OfferingDocumentRequirementFormViewModel
{
    public int ProgramOfferingId { get; set; }
    public Guid PublicId { get; set; }

    [Required(ErrorMessage = "Belge kodu zorunludur.")]
    [StringLength(64, MinimumLength = 2)]
    public string DocumentCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.DisplayNameRequired")]
    [StringLength(150, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public DocumentContentCategory AllowedContentCategory { get; set; } = DocumentContentCategory.PdfOrImage;

    [Range(1, 104857600, ErrorMessage = "Validation.MaximumSize")]
    public long MaximumBytes { get; set; } = 10485760;
    public string? RowVersion { get; set; }
}

public sealed class ReviewApplicationDocumentViewModel
{
    public Guid ApplicationPublicId { get; set; }
    public Guid DocumentPublicId { get; set; }
    public DocumentReviewStatus ReviewStatus { get; set; }
    [StringLength(500)]
    public string? RejectionReason { get; set; }
    [Required]
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class AdminStudentListItemViewModel
{
    public Guid PublicId { get; set; }
    public string MaskedTc { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class AdminStudentListViewModel
{
    public PagedResultViewModel<AdminStudentListItemViewModel> Result { get; set; } = new();
    public string? Search { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class AdminStudentDetailViewModel
{
    public Guid PublicId { get; set; }
    public string MaskedTc { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telephone { get; set; }
    public bool IsActive { get; set; }
    public int ApplicationCount { get; set; }
    public int ExamScoreCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public enum AdminAccountStatus
{
    Active = 1,
    Inactive = 2,
    InvitationPending = 3,
    Locked = 4
}

public sealed class AdminAccountViewModel
{
    public Guid PublicId { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsInvitationPending { get; set; }
    public bool IsLocked { get; set; }
    public int AccessFailedCount { get; set; }
    public bool IsCurrentAdmin { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class InviteAdminViewModel
{
    [Required(ErrorMessage = "Validation.EmailRequired")]
    [EmailAddress(ErrorMessage = "Validation.ValidEmail")]
    [StringLength(254, ErrorMessage = "Validation.EmailMaximumLength")]
    [Display(Name = "Field.AdminEmail")]
    public string Email { get; set; } = string.Empty;
}

public sealed class AdminAccountPageViewModel
{
    public PagedResultViewModel<AdminAccountViewModel> Result { get; set; } = new();
    public InviteAdminViewModel Invite { get; set; } = new();
    public string? Search { get; set; }
    public AdminAccountStatus? Status { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class InstituteAdminViewModel
{
    public int InstituteId { get; set; }
    public string InstituteName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public int ProgramCount { get; set; }
}

public sealed class UniversityFormViewModel
{
    [Required(ErrorMessage = "Validation.UniversityNameRequired")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Validation.UniversityNameLength")]
    [Display(Name = "Field.UniversityName")]
    public string UniversityName { get; set; } = string.Empty;
}

public sealed class UniversityPageViewModel
{
    public IReadOnlyList<UniversityViewModel> Universities { get; set; } = [];
    public UniversityFormViewModel Form { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public sealed class InstituteFormViewModel
{
    public int InstituteId { get; set; }

    [Required(ErrorMessage = "Validation.InstituteNameRequired")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Validation.InstituteNameLength")]
    [Display(Name = "Field.InstituteName")]
    public string InstituteName { get; set; } = string.Empty;

    [StringLength(24, MinimumLength = 12, ErrorMessage = "Validation.InstituteConcurrency")]
    public string? RowVersion { get; set; }
}

public sealed class InstitutePageViewModel
{
    public PagedResultViewModel<InstituteAdminViewModel> Result { get; set; } = new();
    public InstituteFormViewModel Form { get; set; } = new();
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class ProgramAdminViewModel
{
    public int ProgramId { get; set; }
    public int InstituteId { get; set; }
    public string InstituteName { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public string? ProgramNameEnglish { get; set; }
    public string DegreeType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsEffectivelyActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public int OfferingCount { get; set; }
}

public sealed class ProgramFormViewModel
{
    public int ProgramId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Validation.ValidInstitute")]
    [Display(Name = "Field.Institute")]
    public int InstituteId { get; set; }

    [Required(ErrorMessage = "Validation.ProgramNameRequired")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Validation.ProgramNameLength")]
    [Display(Name = "Field.ProgramName")]
    public string ProgramName { get; set; } = string.Empty;

    [StringLength(100, MinimumLength = 2, ErrorMessage = "Validation.ProgramEnglishNameLength")]
    [Display(Name = "Field.ProgramNameEnglish")]
    public string? ProgramNameEnglish { get; set; }

    [Required(ErrorMessage = "Validation.DegreeTypeRequired")]
    [StringLength(50, ErrorMessage = "Validation.DegreeTypeLength")]
    [Display(Name = "Field.DegreeType")]
    public string DegreeType { get; set; } = string.Empty;

    [StringLength(24, MinimumLength = 12, ErrorMessage = "Validation.ProgramConcurrency")]
    public string? RowVersion { get; set; }
}

public sealed class ProgramTranslationItemViewModel
{
    [Range(1, int.MaxValue)]
    public int ProgramId { get; set; }
    public string InstituteName { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public string DegreeType { get; set; } = string.Empty;

    [StringLength(
        100,
        MinimumLength = 2,
        ErrorMessage = "Validation.ProgramEnglishNameLength")]
    [Display(Name = "Field.ProgramNameEnglish")]
    public string? ProgramNameEnglish { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ProgramTranslationPageViewModel
{
    public List<ProgramTranslationItemViewModel> Items { get; set; } = [];
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public string? ErrorMessage { get; set; }
}

public sealed class ProgramTranslationBatchResultViewModel
{
    public int UpdatedCount { get; set; }
}

public sealed class ProgramPageViewModel
{
    public PagedResultViewModel<ProgramAdminViewModel> Result { get; set; } = new();
    public ProgramFormViewModel Form { get; set; } = new();
    public IReadOnlyList<InstituteAdminViewModel> Institutes { get; set; } = [];
    public IReadOnlyList<string> DegreeTypes { get; set; } = ProgramDegreeTypeOptions.Values;
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
    public int? InstituteId { get; set; }
    public string? DegreeType { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class ProgramDegreeTypeOptions
{
    public static IReadOnlyList<string> Values { get; } =
    [
        "Doktora",
        "Tezli Yüksek Lisans",
        "Tezsiz Yüksek Lisans",
        "Uzaktan Tezsiz Yüksek Lisans"
    ];
}
