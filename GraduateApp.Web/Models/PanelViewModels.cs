using System.ComponentModel.DataAnnotations;

namespace GraduateApp.Web.Models;

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
    public int ApplicationId { get; set; }
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

public sealed class PanelDashboardViewModel
{
    public IReadOnlyList<ProgramViewModel> OpenPrograms { get; set; } = [];
    public IReadOnlyList<PanelApplicationViewModel> Applications { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

public sealed class HomeViewModel
{
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
