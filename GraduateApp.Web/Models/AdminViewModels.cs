using System.ComponentModel.DataAnnotations;

namespace GraduateApp.Web.Models;

public sealed class AdminApplicationListItemViewModel
{
    public int ApplicationId { get; set; }
    public string StudentFullName { get; set; } = string.Empty;
    public string MaskedTc { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
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
    public DateTime ApplicationDateUtc { get; set; }
    public ApplicationStatus CurrentStatus { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<ApplicationStatusHistoryViewModel> History { get; set; } = [];
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
