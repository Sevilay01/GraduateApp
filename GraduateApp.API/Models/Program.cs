namespace GraduateApp.API.Models;

public sealed class Program
{
    public int ProgramId { get; set; }
    public int InstituteId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string? ProgramNameEnglish { get; set; }
    public string DegreeType { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<ProgramOffering> Offerings { get; set; } = new List<ProgramOffering>();
    public Institute Institute { get; set; } = null!;
}
