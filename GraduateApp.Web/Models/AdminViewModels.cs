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
    public int AcademicYearStart { get; set; }
    public string AcademicYear { get; set; } = string.Empty;
    public AcademicTerm Term { get; set; }
    public string TermName { get; set; } = string.Empty;
    public DateTime? ApplicationStartUtc { get; set; }
    public DateTime? ApplicationDeadlineUtc { get; set; }
    public int Quota { get; set; }
    public bool IsOpen { get; set; }
    public bool IsArchived { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<ExamRequirementViewModel> ExamRequirements { get; set; } = [];
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
}

public sealed class ProgramOfferingCatalogViewModel
{
    public IReadOnlyList<ProgramCatalogItemViewModel> Programs { get; set; } = [];
    public IReadOnlyList<ExamCatalogItemViewModel> Exams { get; set; } = [];
}

public sealed class ProgramOfferingRequirementInputViewModel
{
    public bool IsConfigured { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir sınav seçiniz.")]
    public int ExamId { get; set; }

    [Range(
        typeof(decimal),
        "0",
        "999.99",
        ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Puan 0 ile 999,99 arasında olmalıdır.")]
    public decimal MinimumScore { get; set; }
    public DateOnly? MinimumValidityDate { get; set; }
    public bool IsRequired { get; set; }
}

public sealed class ProgramOfferingFormViewModel
{
    public int ProgramOfferingId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir program seçiniz.")]
    [Display(Name = "Program")]
    public int ProgramId { get; set; }

    [Range(2000, 2200, ErrorMessage = "Akademik yıl başlangıcı 2000 ile 2200 arasında olmalıdır.")]
    [Display(Name = "Akademik yıl başlangıcı")]
    public int AcademicYearStart { get; set; }

    [EnumDataType(typeof(AcademicTerm), ErrorMessage = "Geçerli bir dönem seçiniz.")]
    [Display(Name = "Dönem")]
    public AcademicTerm Term { get; set; }

    [Required(ErrorMessage = "Başvuru başlangıç tarihi zorunludur.")]
    [DataType(DataType.DateTime)]
    [Display(Name = "Başvuru başlangıcı (İstanbul)")]
    public DateTime ApplicationStartLocal { get; set; }

    [Required(ErrorMessage = "Son başvuru tarihi zorunludur.")]
    [DataType(DataType.DateTime)]
    [Display(Name = "Son başvuru (İstanbul)")]
    public DateTime ApplicationDeadlineLocal { get; set; }

    [Range(1, 100000, ErrorMessage = "Kontenjan 1 ile 100000 arasında olmalıdır.")]
    [Display(Name = "Kontenjan")]
    public int Quota { get; set; }

    [Display(Name = "İlan açık")]
    public bool IsOpen { get; set; }

    [Display(Name = "Arşivle")]
    public bool IsArchived { get; set; }

    [StringLength(
        64,
        ErrorMessage = "İlan eşzamanlılık bilgisi geçersiz.")]
    public string? RowVersion { get; set; }
    public List<ProgramOfferingRequirementInputViewModel> ExamRequirements { get; set; } = [];
    public IReadOnlyList<ProgramCatalogItemViewModel> Programs { get; set; } = [];
    public IReadOnlyList<ExamCatalogItemViewModel> Exams { get; set; } = [];
}

public sealed class ProgramOfferingPageViewModel
{
    public IReadOnlyList<ProgramOfferingAdminViewModel> Offerings { get; set; } = [];
    public ProgramOfferingFormViewModel Form { get; set; } = new();
    public int? AcademicYearStart { get; set; }
    public AcademicTerm? Term { get; set; }
    public bool IncludeArchived { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyDictionary<int, IReadOnlyList<OfferingDocumentRequirementViewModel>> DocumentRequirements { get; set; }
        = new Dictionary<int, IReadOnlyList<OfferingDocumentRequirementViewModel>>();
    public OfferingDocumentRequirementFormViewModel DocumentRequirementForm { get; set; } = new();
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

    [Required(ErrorMessage = "Görünen ad zorunludur.")]
    [StringLength(150, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public DocumentContentCategory AllowedContentCategory { get; set; } = DocumentContentCategory.PdfOrImage;

    [Range(1, 104857600, ErrorMessage = "Maksimum boyut geçersiz.")]
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
    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    [StringLength(254, ErrorMessage = "E-posta en fazla 254 karakter olabilir.")]
    [Display(Name = "Yeni yöneticinin e-posta adresi")]
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

public sealed class InstituteFormViewModel
{
    public int InstituteId { get; set; }

    [Required(ErrorMessage = "Enstitü adı zorunludur.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Enstitü adı 2 ile 100 karakter arasında olmalıdır.")]
    [Display(Name = "Enstitü adı")]
    public string InstituteName { get; set; } = string.Empty;

    [StringLength(24, MinimumLength = 12, ErrorMessage = "Enstitü eşzamanlılık bilgisi geçersiz.")]
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

    [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir enstitü seçiniz.")]
    [Display(Name = "Enstitü")]
    public int InstituteId { get; set; }

    [Required(ErrorMessage = "Program adı zorunludur.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Program adı 2 ile 100 karakter arasında olmalıdır.")]
    [Display(Name = "Program adı")]
    public string ProgramName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Derece türü zorunludur.")]
    [StringLength(50, ErrorMessage = "Derece türü en fazla 50 karakter olabilir.")]
    [Display(Name = "Derece türü")]
    public string DegreeType { get; set; } = string.Empty;

    [StringLength(24, MinimumLength = 12, ErrorMessage = "Program eşzamanlılık bilgisi geçersiz.")]
    public string? RowVersion { get; set; }
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
