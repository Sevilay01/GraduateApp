using GraduateApp.API.Domain;

namespace GraduateApp.API.Models;

public sealed class ProgramOfferingDocumentRequirement
{
    public int RequirementId { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public int ProgramOfferingId { get; set; }
    public string DocumentCode { get; set; } = string.Empty;
    public string NormalizedDocumentCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public DocumentContentCategory AllowedContentCategory { get; set; }
    public long MaximumBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ProgramOffering ProgramOffering { get; set; } = null!;
    public ICollection<ApplicationDocumentRequirementSnapshot> Snapshots { get; set; } = new List<ApplicationDocumentRequirementSnapshot>();
}
