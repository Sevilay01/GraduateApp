using GraduateApp.API.DTOs;
using GraduateApp.Web.Models;

namespace GraduateApp.Tests;

public sealed class AdminApplicationDetailPresentationTests
{
    [Fact]
    public void Pending_exposes_only_under_review_transition()
    {
        Assert.Equal(
            [ApplicationStatus.UnderReview],
            ApplicationStatus.Pending.AllowedAdminTransitions());
    }

    [Fact]
    public void Under_review_exposes_only_approved_and_rejected_transitions()
    {
        Assert.Equal(
            [ApplicationStatus.Approved, ApplicationStatus.Rejected],
            ApplicationStatus.UnderReview.AllowedAdminTransitions());
    }

    [Theory]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void Final_statuses_expose_no_admin_transition(ApplicationStatus status)
    {
        Assert.Empty(status.AllowedAdminTransitions());
    }

    [Fact]
    public void Admin_detail_contracts_expose_masked_tc_instead_of_raw_tc()
    {
        Assert.NotNull(typeof(AdminApplicationDetailDto).GetProperty("MaskedTc"));
        Assert.Null(typeof(AdminApplicationDetailDto).GetProperty("Tc"));
        Assert.NotNull(typeof(AdminApplicationDetailViewModel).GetProperty("MaskedTc"));
        Assert.Null(typeof(AdminApplicationDetailViewModel).GetProperty("Tc"));
    }

    [Fact]
    public void Detail_view_uses_istanbul_time_and_turkish_exam_results_heading()
    {
        var view = ReadDetailView();

        Assert.Contains("IstanbulTime.FromUtc(history.ChangedAtUtc)", view, StringComparison.Ordinal);
        Assert.Contains("(İstanbul)", view, StringComparison.Ordinal);
        Assert.DoesNotContain(">UTC</time>", view, StringComparison.Ordinal);
        Assert.Contains("Başvuru anındaki sınav sonuçları", view, StringComparison.Ordinal);
        Assert.Contains("@Model.MaskedTc", view, StringComparison.Ordinal);
        Assert.DoesNotContain("@Model.Tc", view, StringComparison.Ordinal);
    }

    private static string ReadDetailView()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repositoryRoot, "GraduateApp.Web", "Views", "Admin", "Detail.cshtml"));
    }
}
