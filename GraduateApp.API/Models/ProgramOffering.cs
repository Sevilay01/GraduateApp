using GraduateApp.API.Domain;

namespace GraduateApp.API.Models;

public sealed class ProgramOffering
{
    public int ProgramOfferingId { get; set; }
    public int ProgramId { get; set; }
    public int AcademicYearStart { get; set; }
    public AcademicTerm Term { get; set; }
    public DateTime? ApplicationStartUtc { get; set; }
    public DateTime? ApplicationDeadlineUtc { get; set; }
    public int Quota { get; set; }
    public bool IsOpen { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Program Program { get; set; } = null!;
    public ICollection<Application> Applications { get; set; } = new List<Application>();
    public ICollection<ProgramOfferingDocumentRequirement> DocumentRequirements { get; set; } = new List<ProgramOfferingDocumentRequirement>();
    public ICollection<ProgramOfferingExamRequirement> ExamRequirements { get; set; } = new List<ProgramOfferingExamRequirement>();
}
