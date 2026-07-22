using GraduateApp.Web.Models;

namespace GraduateApp.Tests;

public sealed class DocumentWorkflowInvariantWebTests
{
    [Fact]
    public void Submit_presentation_rejects_a_draft_without_requirement_snapshots()
    {
        var model = Draft([]);

        Assert.False(model.HasValidRequiredDocumentConfiguration());
        Assert.False(model.CanSubmitDocumentWorkflow());
    }

    [Fact]
    public void Submit_presentation_rejects_a_draft_with_only_optional_snapshots()
    {
        var model = Draft([Requirement(isRequired: false, hasCurrentDocument: false)]);

        Assert.False(model.HasValidRequiredDocumentConfiguration());
        Assert.False(model.CanSubmitDocumentWorkflow());
    }

    [Fact]
    public void Submit_presentation_requires_a_current_document_for_every_required_snapshot()
    {
        var missing = Draft([Requirement(isRequired: true, hasCurrentDocument: false)]);
        var complete = Draft([
            Requirement(isRequired: true, hasCurrentDocument: true),
            Requirement(isRequired: false, hasCurrentDocument: false)
        ]);

        Assert.False(missing.CanSubmitDocumentWorkflow());
        Assert.True(complete.CanSubmitDocumentWorkflow());
    }

    [Fact]
    public void Student_view_uses_the_secure_submission_presentation_and_controlled_warning()
    {
        var view = ReadView("Panel", "ApplicationDetail.cshtml");

        Assert.Contains("Model.CanSubmitDocumentWorkflow()", view, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(!canSubmit)\"", view, StringComparison.Ordinal);
        Assert.Contains(
            "Bu taslak için geçerli zorunlu belge koşulu bulunmuyor. İlan yöneticisiyle iletişime geçin.",
            view,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_view_blocks_opening_without_an_active_required_requirement_and_preserves_snapshot_notice()
    {
        var view = ReadView("Admin", "Offerings.cshtml");

        Assert.Contains("item.IsActive && item.IsRequired", view, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(!formOfferingHasActiveRequiredRequirement)\"", view, StringComparison.Ordinal);
        Assert.Contains("Yeni ilan önce kapalı oluşturulur.", view, StringComparison.Ordinal);
        Assert.Contains(
            "İlan açılmadan önce en az bir zorunlu belge koşulu tanımlayın.",
            view,
            StringComparison.Ordinal);
        Assert.Contains("Mevcut taslak ve başvuruların snapshot koşulları değişmez.", view, StringComparison.Ordinal);
    }

    private static StudentApplicationDetailViewModel Draft(
        IReadOnlyList<ApplicationDocumentRequirementViewModel> requirements) => new()
        {
            CurrentStatus = GraduateApp.Web.Models.ApplicationStatus.Draft,
            UsesDocumentWorkflow = true,
            DocumentRequirements = requirements
        };

    private static ApplicationDocumentRequirementViewModel Requirement(
        bool isRequired,
        bool hasCurrentDocument) => new()
        {
            PublicId = Guid.NewGuid(),
            DocumentCode = isRequired ? "TRANSCRIPT" : "PORTFOLIO",
            DisplayName = isRequired ? "Transkript" : "Portfolyo",
            IsRequired = isRequired,
            CurrentDocument = hasCurrentDocument
                ? new ApplicationDocumentViewModel
                {
                    PublicId = Guid.NewGuid(),
                    IsCurrent = true,
                    ReviewStatus = DocumentReviewStatus.Pending
                }
                : null
        };

    private static string ReadView(string folder, string fileName)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repositoryRoot, "GraduateApp.Web", "Views", folder, fileName));
    }
}
