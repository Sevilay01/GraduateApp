namespace GraduateApp.API.Models;

public sealed class Program
{
    public int ProgramId { get; set; }
    public int InstituteId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string? DegreeType { get; set; }
    public bool IsOpen { get; set; }
    public DateTime? ApplicationDeadlineUtc { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();
    public Institute Institute { get; set; } = null!;
}
