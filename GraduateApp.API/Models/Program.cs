namespace GraduateApp.API.Models;

public sealed class Program
{
    public int ProgramId { get; set; }
    public int InstituteId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string? DegreeType { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ProgramOffering> Offerings { get; set; } = new List<ProgramOffering>();
    public Institute Institute { get; set; } = null!;
}
