using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http.Json;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.Localization;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace GraduateApp.Tests;

public sealed class AdminOfferingSubnavigationTests
{
    private static readonly string[] SubpageActions =
    [
        nameof(AdminController.OfferingOverview),
        nameof(AdminController.EditOffering),
        nameof(AdminController.ExamRequirements),
        nameof(AdminController.DocumentRequirements),
        nameof(AdminController.OfferingApplications),
        nameof(AdminController.EvaluationCriteria),
        nameof(AdminController.Evaluation),
        nameof(AdminController.Results)
    ];

    private static readonly string[] MutationActions =
    [
        nameof(AdminController.SaveOffering),
        nameof(AdminController.SaveExamRequirements),
        nameof(AdminController.CloseInvalidOfferingForRemediation),
        nameof(AdminController.SaveDocumentRequirement),
        nameof(AdminController.SetDocumentRequirementActive),
        nameof(AdminController.SaveEvaluationCriterion),
        nameof(AdminController.DeleteEvaluationCriterion),
        nameof(AdminController.DecideEligibility),
        nameof(AdminController.SetManualScore),
        nameof(AdminController.FinalizeEvaluation),
        nameof(AdminController.PublishEvaluationConfirmed)
    ];

    [Fact]
    public void Every_offering_subpage_has_a_direct_get_action_under_admin_authorization()
    {
        var authorize = typeof(AdminController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize!.Roles);
        Assert.DoesNotContain("Student", authorize.Roles, StringComparison.Ordinal);

        foreach (var actionName in SubpageActions)
        {
            var action = typeof(AdminController).GetMethods()
                .Single(method => method.Name == actionName && method.GetCustomAttribute<HttpGetAttribute>() is not null);
            Assert.Contains(action.GetParameters(), parameter => parameter.Name == "id");
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }

    [Fact]
    public void Common_header_uses_real_links_and_marks_only_the_active_page_semantically()
    {
        var view = ReadView("_OfferingHeader.cshtml");

        foreach (var actionName in SubpageActions)
        {
            Assert.Contains($"asp-action=\"{actionName}\"", view, StringComparison.Ordinal);
        }

        Assert.Contains("aria-current=\"@(Model.ActiveSection", view, StringComparison.Ordinal);
        Assert.DoesNotContain("onclick", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-bs-toggle=\"tab\"", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Offering_mutations_remain_post_only_and_antiforgery_protected()
    {
        foreach (var actionName in MutationActions)
        {
            var action = typeof(AdminController).GetMethods().Single(method => method.Name == actionName);
            Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
            Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        }
    }

    [Fact]
    public void Language_switch_return_url_keeps_the_current_subpage_path_query_and_offering_id()
    {
        var layout = File.ReadAllText(
            Path.Combine(Root(), "GraduateApp.Web", "Views", "Shared", "_Layout.cshtml"));
        var header = ReadView("_OfferingHeader.cshtml");

        Assert.Contains("Context.Request.Path", layout, StringComparison.Ordinal);
        Assert.Contains("Context.Request.QueryString", layout, StringComparison.Ordinal);
        Assert.Contains("name=\"returnUrl\" value=\"@returnUrl\"", layout, StringComparison.Ordinal);
        Assert.Contains("asp-route-id=\"@offering.ProgramOfferingId\"", header, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_page_contains_only_its_own_mutation_surface()
    {
        var list = ReadView("Offerings.cshtml");
        var edit = ReadView("EditOffering.cshtml");
        var exams = ReadView("ExamRequirements.cshtml");
        var documents = ReadView("DocumentRequirements.cshtml");
        var applications = ReadView("OfferingApplications.cshtml");
        var criteria = ReadView("EvaluationCriteria.cshtml");
        var evaluation = ReadView("Evaluation.cshtml");

        Assert.DoesNotContain("SaveOffering", list, StringComparison.Ordinal);
        Assert.Contains("SaveOffering", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveExamRequirements", edit, StringComparison.Ordinal);
        Assert.Contains("SaveExamRequirements", exams, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveOffering", exams, StringComparison.Ordinal);
        Assert.Contains("SaveDocumentRequirement", documents, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveEvaluationCriterion", documents, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveDocumentRequirement", applications, StringComparison.Ordinal);
        Assert.Contains("SaveEvaluationCriterion", criteria, StringComparison.Ordinal);
        Assert.DoesNotContain("DecideEligibility", criteria, StringComparison.Ordinal);
        Assert.Contains("DecideEligibility", evaluation, StringComparison.Ordinal);
        Assert.Contains("SetManualScore", evaluation, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveEvaluationCriterion", evaluation, StringComparison.Ordinal);
    }

    [Fact]
    public void Concurrency_tokens_stay_on_each_mutating_offering_subpage()
    {
        Assert.Contains("asp-for=\"Form.RowVersion\"", ReadView("EditOffering.cshtml"), StringComparison.Ordinal);
        Assert.Contains("asp-for=\"Form.RowVersion\"", ReadView("ExamRequirements.cshtml"), StringComparison.Ordinal);
        Assert.Contains(
            "asp-for=\"DocumentRequirementForm.RowVersion\"",
            ReadView("DocumentRequirements.cshtml"),
            StringComparison.Ordinal);
        Assert.Contains("name=\"rowVersion\"", ReadView("OfferingOverview.cshtml"), StringComparison.Ordinal);
        Assert.Contains("name=\"RowVersion\"", ReadView("EvaluationCriteria.cshtml"), StringComparison.Ordinal);
        Assert.Contains("name=\"rowVersion\"", ReadView("Evaluation.cshtml"), StringComparison.Ordinal);
        Assert.Contains("name=\"rowVersion\"", ReadView("PublishEvaluation.cshtml"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Successful_exam_requirement_save_returns_to_its_own_subpage()
    {
        var handler = new ExamRequirementHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        var controller = new AdminController(new GraduateApiClient(client))
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new EmptyTempDataProvider())
        };

        var result = Assert.IsType<RedirectToActionResult>(await controller.SaveExamRequirements(
            new OfferingExamRequirementsFormViewModel
            {
                ProgramOfferingId = ExamRequirementHandler.OfferingId,
                RowVersion = "AQIDBA==",
                Requirements = []
            },
            CancellationToken.None));

        Assert.Equal(nameof(AdminController.ExamRequirements), result.ActionName);
        Assert.Equal(ExamRequirementHandler.OfferingId, result.RouteValues!["id"]);
        Assert.Equal([HttpMethod.Get, HttpMethod.Put], handler.Methods);
    }

    [Fact]
    public void Turkish_and_english_navigation_texts_come_from_matching_resources()
    {
        var manager = new ResourceManager("GraduateApp.Web.Resources.SharedText", typeof(UiText).Assembly);
        var turkish = manager.GetResourceSet(
            System.Globalization.CultureInfo.InvariantCulture,
            createIfNotExists: true,
            tryParents: false)!;
        var english = manager.GetResourceSet(
            System.Globalization.CultureInfo.GetCultureInfo(UiText.EnglishCultureName),
            createIfNotExists: true,
            tryParents: false)!;
        var keys = new[]
        {
            "Admin.OfferingNav.Overview",
            "Admin.OfferingNav.Edit",
            "Admin.OfferingNav.Exams",
            "Admin.OfferingNav.Documents",
            "Admin.OfferingNav.Applications",
            "Admin.OfferingNav.Criteria",
            "Admin.OfferingNav.Evaluation",
            "Admin.OfferingNav.Results"
        };

        Assert.All(keys, key =>
        {
            Assert.False(string.IsNullOrWhiteSpace(turkish.GetString(key)));
            Assert.False(string.IsNullOrWhiteSpace(english.GetString(key)));
        });
        Assert.Equal("Genel Bakış", turkish.GetString(keys[0]));
        Assert.Equal("Overview", english.GetString(keys[0]));
    }

    [Fact]
    public void Offering_views_have_no_duplicate_literal_ids()
    {
        foreach (var file in new[]
                 {
                     "Offerings.cshtml",
                     "OfferingOverview.cshtml",
                     "EditOffering.cshtml",
                     "ExamRequirements.cshtml",
                     "DocumentRequirements.cshtml",
                     "OfferingApplications.cshtml",
                     "EvaluationCriteria.cshtml",
                     "Evaluation.cshtml",
                     "PublishEvaluation.cshtml"
                 })
        {
            var ids = Regex.Matches(ReadView(file), "\\bid=\\\"([a-z][a-z0-9-]*)\\\"", RegexOptions.IgnoreCase)
                .Select(match => match.Groups[1].Value)
                .ToArray();
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void Mobile_subnavigation_is_bounded_and_horizontally_scrollable_without_page_overflow()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "GraduateApp.Web", "wwwroot", "css", "site.css"));

        Assert.Contains(".offering-subnav", css, StringComparison.Ordinal);
        Assert.Contains("max-width: 100%", css, StringComparison.Ordinal);
        Assert.Contains("overflow-x: auto", css, StringComparison.Ordinal);
        Assert.Contains("width: max-content", css, StringComparison.Ordinal);
        Assert.Contains("flex: 0 0 auto", css, StringComparison.Ordinal);
        Assert.Contains("white-space: nowrap", css, StringComparison.Ordinal);
    }

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(Root(), "GraduateApp.Web", "Views", "Admin", fileName));

    private static string Root() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private sealed class ExamRequirementHandler : HttpMessageHandler
    {
        public const int OfferingId = 42;
        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            Assert.Equal($"/api/program-offerings/{OfferingId}", request.RequestUri!.AbsolutePath);
            Assert.Contains(request.Method, new[] { HttpMethod.Get, HttpMethod.Put });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ProgramOfferingAdminViewModel
                {
                    ProgramOfferingId = OfferingId,
                    ProgramId = 7,
                    ProgramName = "Bilgisayar Mühendisliği",
                    DegreeType = "Doktora",
                    AcademicYearStart = 2026,
                    AcademicYear = "2026–2027",
                    Term = AcademicTerm.Fall,
                    TermName = "Güz",
                    Quota = 10,
                    RowVersion = "BQYHCA=="
                })
            });
        }
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) =>
            new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }
}
