using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using GraduateApp.Web.Controllers;
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
    public Task Valid_new_criterion_binds_and_posts_to_the_correct_offering() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            using var host = CreateWebHost();
            using var scope = host.Services.CreateScope();
            var (model, actionContext) = await BindAndValidateAsync(
                scope.ServiceProvider,
                ValidFormValues(maximumRawScore: "100"));
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
            Assert.Equal(100m, body.RootElement.GetProperty("maximumRawScore").GetDecimal());
            Assert.Equal(nameof(AdminController.Evaluation), result.ActionName);
            Assert.Equal(OfferingId, result.RouteValues!["id"]);
        });

    [Fact]
    public Task Existing_criterion_update_keeps_prefixless_public_id_and_row_version_binding() =>
        ExecuteInTurkishCultureAsync(async () =>
        {
            using var host = CreateWebHost();
            using var scope = host.Services.CreateScope();
            var values = ValidFormValues(maximumRawScore: "4");
            values["PublicId"] = CriterionPublicId.ToString("D");
            values["RowVersion"] = "AQIDBAUGBwg=";
            var (model, actionContext) = await BindAndValidateAsync(scope.ServiceProvider, values);
            var handler = new CriterionHandler();
            using var httpClient = Client(handler);
            var controller = CreateController(httpClient, actionContext);

            var result = Assert.IsType<RedirectToActionResult>(
                await controller.SaveEvaluationCriterion(model, CancellationToken.None));

            Assert.True(controller.ModelState.IsValid);
            Assert.Equal(CriterionPublicId, model.PublicId);
            Assert.Equal("AQIDBAUGBwg=", model.RowVersion);
            Assert.Equal(HttpMethod.Put, handler.Method);
            Assert.Equal(
                $"/api/program-offerings/{OfferingId}/evaluation-criteria/{CriterionPublicId:D}",
                handler.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(handler.RequestBody);
            Assert.Equal("AQIDBAUGBwg=", body.RootElement.GetProperty("rowVersion").GetString());
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
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(AdminController).Assembly);
                })
                .Configure(_ => { }))
            .Build();

    private static async Task<string> RenderEvaluationViewAsync(IServiceProvider services)
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
            Model = new EvaluationPageViewModel
            {
                Evaluation = new AdminEvaluationViewModel
                {
                    ProgramOfferingId = OfferingId,
                    ProgramName = "Test Programı",
                    AcademicYear = "2026-2027",
                    TermName = "Güz",
                    EvaluationState = OfferingEvaluationState.Configuring
                },
                CriterionForm = new EvaluationCriterionFormViewModel
                {
                    ProgramOfferingId = OfferingId,
                    SourceType = EvaluationCriterionSourceType.ManualScore,
                    MaximumRawScore = 100m,
                    TieBreakPriority = 1
                }
            }
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
