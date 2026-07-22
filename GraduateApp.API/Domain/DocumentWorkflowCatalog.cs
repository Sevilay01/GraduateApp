namespace GraduateApp.API.Domain;

public enum DocumentContentCategory
{
    PdfOnly,
    ImageOnly,
    PdfOrImage
}

public enum DocumentReviewStatus
{
    Pending,
    Approved,
    Rejected
}

public static class DocumentWorkflowCatalog
{
    public const long DefaultMaximumUploadBytes = 10 * 1024 * 1024;
    public const long AbsoluteMaximumUploadBytes = 100 * 1024 * 1024;

    public static bool Allows(this DocumentContentCategory category, string verifiedContentType) => category switch
    {
        DocumentContentCategory.PdfOnly => verifiedContentType == "application/pdf",
        DocumentContentCategory.ImageOnly => verifiedContentType is "image/jpeg" or "image/png",
        DocumentContentCategory.PdfOrImage => verifiedContentType is "application/pdf" or "image/jpeg" or "image/png",
        _ => false
    };
}
