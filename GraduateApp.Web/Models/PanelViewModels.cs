namespace GraduateApp.Web.Models;

public sealed class ProgramViewModel
{
    public int ProgramId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string? DegreeType { get; set; }
    public string InstituteName { get; set; } = string.Empty;
    public DateTime? ApplicationDeadlineUtc { get; set; }
}

public sealed class PanelApplicationViewModel
{
    public int ApplicationId { get; set; }
    public int ProgramId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
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
