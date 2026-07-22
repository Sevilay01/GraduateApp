using GraduateApp.API.Domain;

namespace GraduateApp.API.Models;

public sealed class ApplicationDocumentRequirementSnapshot
{
    public int SnapshotId { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public int ApplicationId { get; set; }
    public int? SourceRequirementId { get; set; }
    public string DocumentCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public DocumentContentCategory AllowedContentCategory { get; set; }
    public long MaximumBytes { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Application Application { get; set; } = null!;
    public ProgramOfferingDocumentRequirement? SourceRequirement { get; set; }
    public ICollection<ApplicationDocument> Documents { get; set; } = new List<ApplicationDocument>();
}
