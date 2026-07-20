using System.ComponentModel.DataAnnotations;

namespace GraduateApp.Web.Models;

public sealed class AdminApplicationListItemViewModel
{
    public int ApplicationId { get; set; }
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
    public int ApplicationId { get; set; }
    public string Tc { get; set; } = string.Empty;
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

    [Range(1, int.MaxValue)]
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

    [Range(1, int.MaxValue)]
    [Display(Name = "Program")]
    public int ProgramId { get; set; }

    [Range(2000, 2200)]
    [Display(Name = "Akademik yıl başlangıcı")]
    public int AcademicYearStart { get; set; }

    [EnumDataType(typeof(AcademicTerm))]
    [Display(Name = "Dönem")]
    public AcademicTerm Term { get; set; }

    [Required, DataType(DataType.DateTime)]
    [Display(Name = "Başvuru başlangıcı (İstanbul)")]
    public DateTime ApplicationStartLocal { get; set; }

    [Required, DataType(DataType.DateTime)]
    [Display(Name = "Son başvuru (İstanbul)")]
    public DateTime ApplicationDeadlineLocal { get; set; }

    [Range(1, 100000)]
    [Display(Name = "Kontenjan")]
    public int Quota { get; set; }

    [Display(Name = "İlan açık")]
    public bool IsOpen { get; set; }

    [Display(Name = "Arşivle")]
    public bool IsArchived { get; set; }

    [StringLength(64)]
    public string RowVersion { get; set; } = string.Empty;
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
}

public sealed class UpdateApplicationStatusViewModel
{
    [Range(1, int.MaxValue)]
    public int ApplicationId { get; set; }

    [EnumDataType(typeof(ApplicationStatus))]
    [Display(Name = "Yeni durum")]
    public ApplicationStatus NewStatus { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Not")]
    public string? Notes { get; set; }
}
