using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.ModelBinding;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace GraduateApp.Tests;

public sealed class EvaluationCriterionBindingTests
{
    private const int OfferingId = 42;
    private static readonly Guid CriterionPublicId =
        Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public Task New_criterion_form_renders_prefixless_names_validation_metadata_and_antiforgery_under_turkish_culture() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            using var host = CreateWebHost();

            var html = await RenderEvaluationViewAsync(host.Services);
            var decodedHtml = WebUtility.HtmlDecode(html);

            foreach (var name in new[]
                     {
                         "ProgramOfferingId",
                         "Code",
                         "DisplayName",
                         "SourceType",
                         "ExamId",
                         "WeightBasisPoints",
                         "MaximumRawScore",
                         "TieBreakPriority"
                     })
            {
                Assert.Contains($"name=\"{name}\"", html, StringComparison.Ordinal);
                Assert.DoesNotContain($"name=\"CriterionForm.{name}\"", html, StringComparison.Ordinal);
            }

            Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"MaximumRawScore\"", html, StringComparison.Ordinal);
            Assert.Contains("data-val-range-min=\"0.0001\"", html, StringComparison.Ordinal);
            Assert.Contains("data-val-range-max=\"99999\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<input name=\"ExamId\"", html, StringComparison.Ordinal);
            Assert.Contains("Sınav puanı", decodedHtml, StringComparison.Ordinal);
            Assert.Contains("Lisans GNO", decodedHtml, StringComparison.Ordinal);
            Assert.Contains("Manuel puan", decodedHtml, StringComparison.Ordinal);
        });

    [Fact]
    public Task ExamScore_form_renders_only_eligible_exam_options_and_selects_existing_exam() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            using var host = CreateWebHost();

            var html = await RenderEvaluationViewAsync(host.Services, ExistingCriterionPageModel());
            var criterionForm = FormContaining(
                html,
                $"name=\"PublicId\" value=\"{CriterionPublicId:D}\"");
            var examSelect = OpeningTagContaining(criterionForm, "<select name=\"ExamId\"");

            Assert.DoesNotContain("<input name=\"ExamId\"", criterionForm, StringComparison.Ordinal);
            Assert.Contains("required=\"required\"", examSelect, StringComparison.Ordinal);
            Assert.DoesNotContain("disabled", examSelect, StringComparison.Ordinal);
            Assert.Contains("value=\"6\" selected=\"selected\"", criterionForm, StringComparison.Ordinal);
            Assert.Contains("ALES · En az 55,5", criterionForm, StringComparison.Ordinal);
            Assert.DoesNotContain("İlan dışı sınav", criterionForm, StringComparison.Ordinal);
        });

    [Fact]
    public Task Non_exam_forms_render_hidden_disabled_exam_select_and_clear_stale_values_in_script() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            using var host = CreateWebHost();

            var html = await RenderEvaluationViewAsync(host.Services, NonExamCriterionPageModel());
            var gpaForm = FormContaining(html, "name=\"Code\" value=\"GPA\"");
            var newManualForm = FormContaining(html, "<h3 class=\"h6\">Yeni kriter</h3>");

            foreach (var form in new[] { gpaForm, newManualForm })
            {
                var examField = OpeningTagContaining(form, "data-exam-field");
                var examSelect = OpeningTagContaining(form, "<select name=\"ExamId\"");
                Assert.Contains("hidden=\"hidden\"", examField, StringComparison.Ordinal);
                Assert.Contains("disabled=\"disabled\"", examSelect, StringComparison.Ordinal);
                Assert.DoesNotContain("required", examSelect, StringComparison.Ordinal);
                Assert.DoesNotContain("<input name=\"ExamId\"", form, StringComparison.Ordinal);
            }

            Assert.Contains("examId.disabled = !usesExam;", html, StringComparison.Ordinal);
            Assert.Contains("examId.value = '';", html, StringComparison.Ordinal);
            Assert.Contains("sourceType.addEventListener('change'", html, StringComparison.Ordinal);
        });

    [Fact]
    public Task Existing_scaled_values_render_as_canonical_decimal_inputs_under_turkish_culture() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            using var host = CreateWebHost();

            var html = await RenderEvaluationViewAsync(host.Services, ExistingCriterionPageModel());
            var criterionForm = FormContaining(
                html,
                $"name=\"PublicId\" value=\"{CriterionPublicId:D}\"");
            var manualScoreForm = FormContaining(
                html,
                $"name=\"criterionPublicId\" value=\"{CriterionPublicId:D}\"");

            Assert.Contains(
                "name=\"MaximumRawScore\" value=\"100\"",
                criterionForm,
                StringComparison.Ordinal);
            Assert.Contains(
                "name=\"WeightBasisPoints\" value=\"55\"",
                criterionForm,
                StringComparison.Ordinal);
            Assert.Contains("name=\"__RequestVerificationToken\"", criterionForm, StringComparison.Ordinal);
            Assert.DoesNotContain("100,000", criterionForm, StringComparison.Ordinal);
            Assert.Contains(
                "name=\"rawScore\" value=\"87.5\"",
                manualScoreForm,
                StringComparison.Ordinal);
        });

    [Theory]
    [InlineData(OfferingEvaluationState.Finalized, "Sonuçlar kesinleştirildi; değerlendirme verileri değiştirilemez.")]
    [InlineData(OfferingEvaluationState.Published, "Sonuçlar yayımlandı; değerlendirme verileri değiştirilemez.")]
    public Task Locked_lifecycle_renders_values_without_any_evaluation_mutation_surface(
        OfferingEvaluationState state,
        string expectedMessage) =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            using var host = CreateWebHost();

            var html = await RenderEvaluationViewAsync(host.Services, LockedPageModel(state));
            var decodedHtml = WebUtility.HtmlDecode(html);

            Assert.Contains(expectedMessage, decodedHtml, StringComparison.Ordinal);
            Assert.Contains("data-readonly-evaluation-criterion", html, StringComparison.Ordinal);
            Assert.Contains("data-readonly-eligibility", html, StringComparison.Ordinal);
            Assert.Contains("data-readonly-evaluation-components", html, StringComparison.Ordinal);
            Assert.Contains("ALES", decodedHtml, StringComparison.Ordinal);
            Assert.Contains("Lisans GNO", decodedHtml, StringComparison.Ordinal);
            Assert.Contains("Mülakat", decodedHtml, StringComparison.Ordinal);
            Assert.Contains("87,5", decodedHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("<input", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<select", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SaveEvaluationCriterion", html, StringComparison.Ordinal);
            Assert.DoesNotContain("DeleteEvaluationCriterion", html, StringComparison.Ordinal);
            Assert.DoesNotContain("DecideEligibility", html, StringComparison.Ordinal);
            Assert.DoesNotContain("SetManualScore", html, StringComparison.Ordinal);
            Assert.DoesNotContain("FinalizeEvaluation", html, StringComparison.Ordinal);

            if (state == OfferingEvaluationState.Finalized)
            {
                Assert.Contains("PublishEvaluation", html, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("PublishEvaluation", html, StringComparison.Ordinal);
            }
        });

    [Fact]
    public Task Invalid_new_criterion_binds_real_offering_id_redirects_back_and_does_not_call_api() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            using var host = CreateWebHost();
            using var scope = host.Services.CreateScope();
            var (model, actionContext) = await BindAndValidateAsync(
                scope.ServiceProvider,
                ValidFormValues(maximumRawScore: "0"));
            var handler = new CriterionHandler();
            using var httpClient = Client(handler);
            var controller = CreateController(httpClient, actionContext);

            var result = Assert.IsType<RedirectToActionResult>(
                await controller.SaveEvaluationCriterion(model, CancellationToken.None));

            Assert.Equal(OfferingId, model.ProgramOfferingId);
            Assert.False(controller.ModelState.IsValid);
            Assert.Equal(nameof(AdminController.Evaluation), result.ActionName);
            Assert.Equal(OfferingId, result.RouteValues!["id"]);
            Assert.Equal("Değerlendirme kriteri bilgileri geçersiz.", controller.TempData["ErrorMessage"]);
            Assert.Equal(0, handler.RequestCount);
        });

    [Fact]
    public Task Gpa_create_binds_null_exam_and_posts_maximum_four_to_the_correct_offering() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            using var host = CreateWebHost();
            using var scope = host.Services.CreateScope();
            var values = ValidFormValues(maximumRawScore: "4");
            values.Remove("ExamId");
            var (model, actionContext) = await BindAndValidateAsync(scope.ServiceProvider, values);
            var handler = new CriterionHandler();
            using var httpClient = Client(handler);
            var controller = CreateController(httpClient, actionContext);

            var result = Assert.IsType<RedirectToActionResult>(
                await controller.SaveEvaluationCriterion(model, CancellationToken.None));

            Assert.True(controller.ModelState.IsValid);
            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal($"/api/program-offerings/{OfferingId}/evaluation-criteria", handler.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(handler.RequestBody);
            Assert.Equal("GPA", body.RootElement.GetProperty("code").GetString());
            Assert.Equal(EvaluationCriterionSourceType.UndergraduateGpa, model.SourceType);
            Assert.Null(model.ExamId);
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("examId").ValueKind);
            Assert.Equal(4m, body.RootElement.GetProperty("maximumRawScore").GetDecimal());
            Assert.Equal(nameof(AdminController.Evaluation), result.ActionName);
            Assert.Equal(OfferingId, result.RouteValues!["id"]);
        });

    [Fact]
    public Task Manual_create_after_source_change_does_not_carry_stale_exam_id() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            using var host = CreateWebHost();
            using var scope = host.Services.CreateScope();
            var values = ValidFormValues(maximumRawScore: "100");
            values["Code"] = "INTERVIEW";
            values["DisplayName"] = "Mülakat";
            values["SourceType"] = EvaluationCriterionSourceType.ManualScore.ToString();
            values["ExamId"] = "6";
            values.Remove("ExamId");
            var (model, actionContext) = await BindAndValidateAsync(scope.ServiceProvider, values);
            var handler = new CriterionHandler();
            using var httpClient = Client(handler);
            var controller = CreateController(httpClient, actionContext);

            await controller.SaveEvaluationCriterion(model, CancellationToken.None);

            Assert.True(controller.ModelState.IsValid);
            Assert.Null(model.ExamId);
            Assert.Equal(HttpMethod.Post, handler.Method);
            using var body = JsonDocument.Parse(handler.RequestBody);
            Assert.Equal("ManualScore", body.RootElement.GetProperty("sourceType").GetString());
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("examId").ValueKind);
            Assert.Equal(100m, body.RootElement.GetProperty("maximumRawScore").GetDecimal());
        });

    [Fact]
    public Task ExamScore_create_binds_and_posts_selected_offering_exam() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            using var host = CreateWebHost();
            using var scope = host.Services.CreateScope();
            var values = ValidFormValues(maximumRawScore: "100");
            values["Code"] = "ALES";
            values["DisplayName"] = "ALES";
            values["SourceType"] = EvaluationCriterionSourceType.ExamScore.ToString();
            values["ExamId"] = "6";
            var (model, actionContext) = await BindAndValidateAsync(scope.ServiceProvider, values);
            var handler = new CriterionHandler();
            using var httpClient = Client(handler);
            var controller = CreateController(httpClient, actionContext);

            await controller.SaveEvaluationCriterion(model, CancellationToken.None);

            Assert.True(controller.ModelState.IsValid);
            Assert.Equal(6, model.ExamId);
            Assert.Equal(HttpMethod.Post, handler.Method);
            using var body = JsonDocument.Parse(handler.RequestBody);
            Assert.Equal("ExamScore", body.RootElement.GetProperty("sourceType").GetString());
            Assert.Equal(6, body.RootElement.GetProperty("examId").GetInt32());
        });

    [Fact]
    public Task Existing_criterion_update_keeps_prefixless_public_id_and_row_version_binding() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            using var host = CreateWebHost();
            using var scope = host.Services.CreateScope();
            var values = ValidFormValues(maximumRawScore: "100.0000");
            values["PublicId"] = CriterionPublicId.ToString("D");
            values["RowVersion"] = "AQIDBAUGBwg=";
            values["SourceType"] = EvaluationCriterionSourceType.ExamScore.ToString();
            values["ExamId"] = "6";
            values["WeightBasisPoints"] = "5000";
            var (model, actionContext) = await BindAndValidateAsync(scope.ServiceProvider, values);
            var handler = new CriterionHandler();
            using var httpClient = Client(handler);
            var controller = CreateController(httpClient, actionContext);

            var result = Assert.IsType<RedirectToActionResult>(
                await controller.SaveEvaluationCriterion(model, CancellationToken.None));

            Assert.True(controller.ModelState.IsValid);
            Assert.Equal(CriterionPublicId, model.PublicId);
            Assert.Equal("AQIDBAUGBwg=", model.RowVersion);
            Assert.Equal(100m, model.MaximumRawScore);
            Assert.Equal(5000, model.WeightBasisPoints);
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(HttpMethod.Put, handler.Method);
            Assert.Equal(
                $"/api/program-offerings/{OfferingId}/evaluation-criteria/{CriterionPublicId:D}",
                handler.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(handler.RequestBody);
            Assert.Equal("AQIDBAUGBwg=", body.RootElement.GetProperty("rowVersion").GetString());
            Assert.Equal(100m, body.RootElement.GetProperty("maximumRawScore").GetDecimal());
            Assert.Equal(5000, body.RootElement.GetProperty("weightBasisPoints").GetInt32());
            Assert.Equal("Değerlendirme kriteri kaydedildi.", controller.TempData["SuccessMessage"]);
            Assert.Equal(nameof(AdminController.Evaluation), result.ActionName);
            Assert.Equal(OfferingId, result.RouteValues!["id"]);
        });

    [Fact]
    public Task Manual_score_binds_canonical_decimal_and_posts_exact_value_to_api() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            using var host = CreateWebHost();
            using var scope = host.Services.CreateScope();
            var (rawScore, actionContext) = await BindNullableDecimalAsync(
                scope.ServiceProvider,
                "rawScore",
                "87.5");
            var handler = new CriterionHandler();
            using var httpClient = Client(handler);
            var controller = CreateController(httpClient, actionContext);
            var applicationPublicId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            var result = Assert.IsType<RedirectToActionResult>(
                await controller.SetManualScore(
                    OfferingId,
                    applicationPublicId,
                    CriterionPublicId,
                    rawScore,
                    "EBESExQVFhc=",
                    CancellationToken.None));

            Assert.Equal(87.5m, rawScore);
            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal(
                $"/api/evaluations/applications/{applicationPublicId:D}/criteria/{CriterionPublicId:D}/score",
                handler.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(handler.RequestBody);
            Assert.Equal(87.5m, body.RootElement.GetProperty("rawScore").GetDecimal());
            Assert.Equal("EBESExQVFhc=", body.RootElement.GetProperty("rowVersion").GetString());
            Assert.Equal(nameof(AdminController.Evaluation), result.ActionName);
            Assert.Equal(OfferingId, result.RouteValues!["id"]);
        });

    private static IHost CreateWebHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureWebHostDefaults(builder => builder
                .UseEnvironment("Development")
                .UseContentRoot(Path.Combine(RepositoryRoot(), "GraduateApp.Web"))
                .ConfigureServices(services =>
                {
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                    services.AddControllersWithViews(options =>
                        options.ModelBinderProviders.Insert(0, new SafeDecimalModelBinderProvider()))
                        .AddApplicationPart(typeof(AdminController).Assembly);
                })
                .Configure(_ => { }))
            .Build();

    private static async Task<string> RenderEvaluationViewAsync(
        IServiceProvider services,
        EvaluationPageViewModel? model = null)
    {
        using var scope = services.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var httpContext = new DefaultHttpContext { RequestServices = scopedServices };
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost");
        var routeData = new RouteData();
        routeData.Values["controller"] = "Admin";
        routeData.Values["action"] = nameof(AdminController.Evaluation);
        routeData.Routers.Add(new TestRouter());
        var actionContext = new ActionContext(
            httpContext,
            routeData,
            new ActionDescriptor(),
            new ModelStateDictionary());
        var viewEngine = scopedServices.GetRequiredService<ICompositeViewEngine>();
        var viewResult = viewEngine.FindView(actionContext, "Evaluation", isMainPage: false);
        Assert.True(
            viewResult.Success,
            $"Evaluation view bulunamadı: {string.Join(", ", viewResult.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<EvaluationPageViewModel>(
            scopedServices.GetRequiredService<IModelMetadataProvider>(),
            actionContext.ModelState)
        {
            Model = model ?? DefaultPageModel()
        };
        var tempData = new TempDataDictionary(
            httpContext,
            scopedServices.GetRequiredService<ITempDataProvider>());
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewData,
            tempData,
            writer,
            new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    private static EvaluationPageViewModel DefaultPageModel() =>
        new()
        {
            Evaluation = new AdminEvaluationViewModel
            {
                ProgramOfferingId = OfferingId,
                ProgramName = "Test Programı",
                AcademicYear = "2026-2027",
                TermName = "Güz",
                EvaluationState = OfferingEvaluationState.Configuring,
                Capabilities = new EvaluationCapabilitiesViewModel
                {
                    CanEditPolicy = true,
                    CanDecideEligibility = true,
                    CanEditManualScore = true,
                    CanFinalize = true
                },
                EligibleExamRequirements =
                [
                    new ExamRequirementViewModel
                    {
                        ExamId = 6,
                        ExamName = "ALES",
                        MinimumScore = 55.5m,
                        IsRequired = true
                    }
                ]
            },
            CriterionForm = new EvaluationCriterionFormViewModel
            {
                ProgramOfferingId = OfferingId,
                SourceType = EvaluationCriterionSourceType.ManualScore,
                MaximumRawScore = 100m,
                TieBreakPriority = 1
            }
        };

    private static EvaluationPageViewModel ExistingCriterionPageModel()
    {
        var model = DefaultPageModel();
        model.Evaluation.Criteria =
        [
            new EvaluationCriterionViewModel
            {
                PublicId = CriterionPublicId,
                Code = "ALES",
                DisplayName = "ALES",
                SourceType = EvaluationCriterionSourceType.ExamScore,
                ExamId = 6,
                WeightBasisPoints = 55,
                MaximumRawScore = 100.0000m,
                TieBreakPriority = 1,
                RowVersion = "AQIDBAUGBwg="
            }
        ];
        model.Evaluation.Applications =
        [
            new AdminEvaluationApplicationViewModel
            {
                ApplicationPublicId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                StudentFullName = "Test Aday",
                CurrentStatus = ApplicationStatus.UnderReview,
                EvaluationRowVersion = "CAkKCwwNDg8=",
                Components =
                [
                    new EvaluationComponentViewModel
                    {
                        CriterionPublicId = CriterionPublicId,
                        DisplayName = "Mülakat",
                        SourceType = EvaluationCriterionSourceType.ManualScore,
                        RawScore = 87.5000m,
                        MaximumRawScore = 100m,
                        WeightBasisPoints = 5000,
                        TieBreakPriority = 1,
                        RowVersion = "EBESExQVFhc="
                    }
                ]
            }
        ];
        return model;
    }

    private static EvaluationPageViewModel LockedPageModel(OfferingEvaluationState state)
    {
        var model = ExistingCriterionPageModel();
        model.Evaluation.EvaluationState = state;
        model.Evaluation.Capabilities = new EvaluationCapabilitiesViewModel
        {
            CanPublish = state == OfferingEvaluationState.Finalized
        };
        model.Evaluation.Criteria =
        [
            .. model.Evaluation.Criteria,
            new EvaluationCriterionViewModel
            {
                PublicId = Guid.Parse("21111111-2222-3333-4444-555555555555"),
                Code = "GPA",
                DisplayName = "Lisans GNO",
                SourceType = EvaluationCriterionSourceType.UndergraduateGpa,
                WeightBasisPoints = 2500,
                MaximumRawScore = 4m,
                TieBreakPriority = 2
            },
            new EvaluationCriterionViewModel
            {
                PublicId = Guid.Parse("31111111-2222-3333-4444-555555555555"),
                Code = "INTERVIEW",
                DisplayName = "Mülakat",
                SourceType = EvaluationCriterionSourceType.ManualScore,
                WeightBasisPoints = 2500,
                MaximumRawScore = 100m,
                TieBreakPriority = 3
            }
        ];
        var application = model.Evaluation.Applications.Single();
        application.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        application.Components =
        [
            new EvaluationComponentViewModel
            {
                CriterionPublicId = CriterionPublicId,
                DisplayName = "ALES",
                SourceType = EvaluationCriterionSourceType.ExamScore,
                RawScore = 75m,
                NormalizedScore = 75m,
                WeightedScore = 41.25m
            },
            new EvaluationComponentViewModel
            {
                CriterionPublicId = Guid.Parse("21111111-2222-3333-4444-555555555555"),
                DisplayName = "Lisans GNO",
                SourceType = EvaluationCriterionSourceType.UndergraduateGpa,
                RawScore = 3.5m,
                NormalizedScore = 87.5m,
                WeightedScore = 21.875m
            },
            new EvaluationComponentViewModel
            {
                CriterionPublicId = Guid.Parse("31111111-2222-3333-4444-555555555555"),
                DisplayName = "Mülakat",
                SourceType = EvaluationCriterionSourceType.ManualScore,
                RawScore = 87.5m,
                NormalizedScore = 87.5m,
                WeightedScore = 21.875m
            }
        ];
        return model;
    }

    private static EvaluationPageViewModel NonExamCriterionPageModel()
    {
        var model = DefaultPageModel();
        model.Evaluation.Criteria =
        [
            new EvaluationCriterionViewModel
            {
                PublicId = CriterionPublicId,
                Code = "GPA",
                DisplayName = "Lisans GNO",
                SourceType = EvaluationCriterionSourceType.UndergraduateGpa,
                ExamId = null,
                WeightBasisPoints = 5000,
                MaximumRawScore = 4m,
                TieBreakPriority = 1,
                RowVersion = "AQIDBAUGBwg="
            }
        ];
        return model;
    }

    private static string FormContaining(string html, string marker)
    {
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Form marker bulunamadı: {marker}");
        var formStart = html.LastIndexOf("<form", markerIndex, StringComparison.Ordinal);
        var formEnd = html.IndexOf("</form>", markerIndex, StringComparison.Ordinal);
        Assert.True(formStart >= 0 && formEnd >= formStart, $"Form sınırları bulunamadı: {marker}");
        return html[formStart..(formEnd + "</form>".Length)];
    }

    private static string OpeningTagContaining(string html, string marker)
    {
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Element marker bulunamadı: {marker}");
        var tagStart = html.LastIndexOf('<', markerIndex);
        var tagEnd = html.IndexOf('>', markerIndex);
        Assert.True(tagStart >= 0 && tagEnd >= tagStart, $"Element sınırları bulunamadı: {marker}");
        return html[tagStart..(tagEnd + 1)];
    }

    private static async Task<(EvaluationCriterionFormViewModel Model, ActionContext ActionContext)> BindAndValidateAsync(
        IServiceProvider services,
        Dictionary<string, StringValues> values)
    {
        var form = new FormCollection(values);
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Features.Set<IFormFeature>(new FormFeature(form));
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ControllerActionDescriptor(),
            new ModelStateDictionary());
        var metadataProvider = services.GetRequiredService<IModelMetadataProvider>();
        var metadata = metadataProvider.GetMetadataForType(typeof(EvaluationCriterionFormViewModel));
        var binder = services.GetRequiredService<IModelBinderFactory>().CreateBinder(
            new ModelBinderFactoryContext
            {
                Metadata = metadata,
                BindingInfo = new BindingInfo()
            });
        var valueProvider = new FormValueProvider(
            BindingSource.Form,
            form,
            CultureInfo.GetCultureInfo("tr-TR"));
        var bindingContext = DefaultModelBindingContext.CreateBindingContext(
            actionContext,
            valueProvider,
            metadata,
            bindingInfo: null,
            modelName: string.Empty);

        await binder.BindModelAsync(bindingContext);
        var model = Assert.IsType<EvaluationCriterionFormViewModel>(bindingContext.Result.Model);
        services.GetRequiredService<IObjectModelValidator>()
            .Validate(actionContext, validationState: null, prefix: string.Empty, model);
        return (model, actionContext);
    }

    private static async Task<(decimal? Value, ActionContext ActionContext)> BindNullableDecimalAsync(
        IServiceProvider services,
        string modelName,
        string attemptedValue)
    {
        var form = new FormCollection(
            new Dictionary<string, StringValues>(StringComparer.Ordinal)
            {
                [modelName] = attemptedValue
            });
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Features.Set<IFormFeature>(new FormFeature(form));
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ControllerActionDescriptor(),
            new ModelStateDictionary());
        var metadata = services.GetRequiredService<IModelMetadataProvider>()
            .GetMetadataForType(typeof(decimal?));
        var binder = services.GetRequiredService<IModelBinderFactory>()
            .CreateBinder(
                new ModelBinderFactoryContext
                {
                    Metadata = metadata,
                    BindingInfo = new BindingInfo()
                });
        var valueProvider = new FormValueProvider(
            BindingSource.Form,
            form,
            CultureInfo.GetCultureInfo("tr-TR"));
        var bindingContext = DefaultModelBindingContext.CreateBindingContext(
            actionContext,
            valueProvider,
            metadata,
            bindingInfo: null,
            modelName);

        await binder.BindModelAsync(bindingContext);
        Assert.True(bindingContext.Result.IsModelSet);
        Assert.Empty(actionContext.ModelState[modelName]!.Errors);
        return ((decimal?)bindingContext.Result.Model, actionContext);
    }

    private static AdminController CreateController(HttpClient httpClient, ActionContext actionContext) =>
        new(new GraduateApiClient(httpClient))
        {
            ControllerContext = new ControllerContext(actionContext),
            TempData = new TempDataDictionary(actionContext.HttpContext, new MemoryTempDataProvider())
        };

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://api.example.test/") };

    private static Dictionary<string, StringValues> ValidFormValues(string maximumRawScore) =>
        new(StringComparer.Ordinal)
        {
            ["ProgramOfferingId"] = OfferingId.ToString(CultureInfo.InvariantCulture),
            ["Code"] = "GPA",
            ["DisplayName"] = "Lisans GNO",
            ["SourceType"] = EvaluationCriterionSourceType.UndergraduateGpa.ToString(),
            ["ExamId"] = string.Empty,
            ["WeightBasisPoints"] = "10000",
            ["MaximumRawScore"] = maximumRawScore,
            ["TieBreakPriority"] = "1"
        };

    private static async Task ExecuteInTurkishCultureAsync(Func<Task> action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            await action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private sealed class CriterionHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>(StringComparer.Ordinal);

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class TestRouter : IRouter
    {
        public Task RouteAsync(RouteContext context) => Task.CompletedTask;

        public VirtualPathData GetVirtualPath(VirtualPathContext context) =>
            new(this, $"/Admin/{context.Values["action"]}");
    }
}
