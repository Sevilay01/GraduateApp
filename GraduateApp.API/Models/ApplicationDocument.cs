using GraduateApp.API.Domain;

namespace GraduateApp.API.Models;

public sealed class ApplicationDocument
{
    public long DocumentId { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public int ApplicationId { get; set; }
    public int RequirementSnapshotId { get; set; }
    public int VersionNumber { get; set; }
    public bool IsCurrent { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string VerifiedContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DocumentReviewStatus ReviewStatus { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public int? ReviewedByAdminId { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Application Application { get; set; } = null!;
    public ApplicationDocumentRequirementSnapshot RequirementSnapshot { get; set; } = null!;
    public Admin? ReviewedByAdmin { get; set; }
}
