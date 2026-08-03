using System.Reflection;
using GraduateApp.API.Controllers;
using GraduateApp.API.DTOs;
using GraduateApp.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Tests;

public sealed class EvaluationWorkflowWebContractTests
{
    [Fact]
    public void Evaluation_api_is_admin_only_and_student_result_is_student_only()
    {
        var admin = typeof(EvaluationsController).GetCustomAttribute<AuthorizeAttribute>();
        var studentResult = typeof(ApplicationsController)
            .GetMethod(nameof(ApplicationsController.GetMineEvaluationResult))!
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal("Admin", admin!.Roles);
        Assert.DoesNotContain("Student", admin.Roles, StringComparison.Ordinal);
        Assert.Equal("Student", studentResult!.Roles);
        Assert.DoesNotContain("Admin", studentResult.Roles, StringComparison.Ordinal);
    }

    [Fact]
    public void Student_base_application_contract_contains_no_score_rank_or_outcome()
    {
        var names = typeof(StudentApplicationDetailDto).GetProperties().Select(item => item.Name).ToArray();

        Assert.DoesNotContain("TotalScore", names);
        Assert.DoesNotContain("Rank", names);
        Assert.DoesNotContain("Outcome", names);
        Assert.DoesNotContain("Evaluation", names);
    }

    [Fact]
    public void Student_view_hides_evaluation_until_separate_published_result_is_available()
    {
        var view = ReadView("Panel", "ApplicationDetail.cshtml");
        var controller = ReadSource("GraduateApp.Web", "Controllers", "PanelController.cs");

        Assert.Contains("Model.PublishedEvaluation is null", view, StringComparison.Ordinal);
        Assert.Contains("ApplicationDetail.EvaluationPendingLead", view, StringComparison.Ordinal);
        Assert.Contains("ApplicationDetail.PublishedResult", view, StringComparison.Ordinal);
        Assert.Contains("GetMyPublishedEvaluationAsync", controller, StringComparison.Ordinal);
        Assert.Contains("if (published.IsSuccess)", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_application_view_does_not_offer_manual_approved_or_rejected_transitions_for_evaluation_applications()
    {
        var view = ReadView("Admin", "Detail.cshtml");

        Assert.Contains("Model.UsesEvaluationWorkflow", view, StringComparison.Ordinal);
        Assert.Contains("item == ApplicationStatus.UnderReview", view, StringComparison.Ordinal);
        Assert.Contains("kesinleştirilip yayımlandığında atomik olarak uygulanır", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluation_web_mutations_are_post_only_and_antiforgery_protected()
    {
        var methodNames = new[]
        {
            nameof(AdminController.SaveEvaluationCriterion),
            nameof(AdminController.DeleteEvaluationCriterion),
            nameof(AdminController.DecideEligibility),
            nameof(AdminController.SetManualScore),
            nameof(AdminController.FinalizeEvaluation),
            nameof(AdminController.PublishEvaluationConfirmed)
        };

        foreach (var methodName in methodNames)
        {
            var method = typeof(AdminController).GetMethod(methodName);
            Assert.NotNull(method);
            Assert.NotNull(method!.GetCustomAttribute<HttpPostAttribute>());
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        }
    }

    [Fact]
    public void Publish_requires_a_separate_confirmation_page_and_row_version()
    {
        var evaluationView = ReadView("Admin", "Evaluation.cshtml");
        var publishView = ReadView("Admin", "PublishEvaluation.cshtml");

        Assert.Contains("asp-action=\"PublishEvaluation\"", evaluationView, StringComparison.Ordinal);
        Assert.Contains("İşlem geri alınamaz", publishView, StringComparison.Ordinal);
        Assert.Contains("name=\"rowVersion\"", publishView, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", publishView, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_evaluation_view_exposes_policy_candidate_preview_finalize_and_publish_sections()
    {
        var view = ReadView("Admin", "Evaluation.cshtml");

        Assert.Contains("Değerlendirme politikası", view, StringComparison.Ordinal);
        Assert.Contains("Kriterler ilk taslak başvuru oluştuğunda kilitlenir", view, StringComparison.Ordinal);
        Assert.Contains("Adaylar", view, StringComparison.Ordinal);
        Assert.Contains("Deterministik sıralama önizlemesi", view, StringComparison.Ordinal);
        Assert.Contains("Sonuçları kesinleştir", view, StringComparison.Ordinal);
        Assert.Contains("Yayımlama onayına geç", view, StringComparison.Ordinal);
    }

    private static string ReadView(string folder, string fileName) =>
        File.ReadAllText(Path.Combine(Root(), "GraduateApp.Web", "Views", folder, fileName));

    private static string ReadSource(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(path).ToArray()));

    private static string Root() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
