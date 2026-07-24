using System.Globalization;
using System.Net;
using System.Security.Claims;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GraduateApp.Tests;

public sealed class GraduateUiFoundationRazorTests
{
    [Fact]
    public async Task Anonymous_home_renders_local_brand_skip_link_active_navigation_and_encoded_content()
    {
        using var host = CreateWebHost();
        const string unsafeProgramName = "<script>alert('program')</script>";
        var html = await RenderMainViewAsync(
            host.Services,
            "Home",
            "Index",
            new HomeViewModel
            {
                OpenPrograms =
                [
                    new ProgramViewModel
                    {
                        ProgramOfferingId = 17,
                        ProgramName = unsafeProgramName,
                        InstituteName = "<b>Fen Bilimleri Enstitüsü</b>",
                        DegreeType = "Yüksek lisans",
                        AcademicYear = "2026–2027",
                        TermName = "Güz",
                        Quota = 12,
                        ApplicationDeadlineUtc = new DateTime(
                            2026,
                            8,
                            24,
                            14,
                            0,
                            0,
                            DateTimeKind.Utc)
                    }
                ]
            });

        Assert.Contains("<html lang=\"tr\">", html, StringComparison.Ordinal);
        Assert.Contains("class=\"skip-link\" href=\"#main-content\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/images/brand/cu_logo_tr.svg\"", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"Çukurova Üniversitesi\"", html, StringComparison.Ordinal);
        Assert.Contains("Öğrenci girişi", html, StringComparison.Ordinal);
        Assert.Contains("Yönetici girişi", html, StringComparison.Ordinal);
        Assert.Contains("Öğrenci kaydı", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Güvenli çıkış", html, StringComparison.Ordinal);
        Assert.Contains(
            "aria-current=\"page\"",
            OpeningTagForLinkText(html, "Ana sayfa"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Fen Bilimleri Enstitüsü</b>", html, StringComparison.Ordinal);
        Assert.Contains(
            "<b>Fen Bilimleri Enstitüsü</b>",
            WebUtility.HtmlDecode(html),
            StringComparison.Ordinal);
        Assert.True(
            html.IndexOf("<h1", StringComparison.Ordinal) < html.IndexOf("<h2", StringComparison.Ordinal),
            "Ana sayfadaki h1, bölüm h2 başlığından önce gelmelidir.");
    }

    [Fact]
    public async Task Student_layout_and_panel_render_role_navigation_post_forms_and_antiforgery()
    {
        using var host = CreateWebHost();
        var applicationPublicId = Guid.Parse("821DF0BB-3299-4778-A9CB-E7C1EBD377B1");
        var html = await RenderMainViewAsync(
            host.Services,
            "Panel",
            "Index",
            new PanelDashboardViewModel
            {
                OpenPrograms =
                [
                    new ProgramViewModel
                    {
                        ProgramOfferingId = 42,
                        ProgramName = "Bilgisayar Mühendisliği",
                        InstituteName = "Fen Bilimleri Enstitüsü",
                        DegreeType = "Doktora",
                        AcademicYear = "2026–2027",
                        TermName = "Güz",
                        Quota = 8,
                        ApplicationDeadlineUtc = new DateTime(
                            2026,
                            8,
                            24,
                            14,
                            0,
                            0,
                            DateTimeKind.Utc)
                    }
                ],
                Applications =
                [
                    new PanelApplicationViewModel
                    {
                        PublicId = applicationPublicId,
                        ProgramOfferingId = 43,
                        ProgramName = "İstatistik",
                        AcademicYear = "2026–2027",
                        TermName = "Güz",
                        ApplicationDateUtc = new DateTime(
                            2026,
                            7,
                            24,
                            10,
                            0,
                            0,
                            DateTimeKind.Utc),
                        CurrentStatus = ApplicationStatus.Pending
                    }
                ]
            },
            AuthenticatedUser("Student", "<Öğrenci Kullanıcı>"));

        Assert.Contains("Başvurularım", html, StringComparison.Ordinal);
        Assert.Contains("Sınav sonuçlarım", html, StringComparison.Ordinal);
        Assert.Contains("Profilim", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Yönetici hesapları", html, StringComparison.Ordinal);
        Assert.Contains(
            "aria-current=\"page\"",
            OpeningTagForLinkText(html, "Başvurularım"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("<Öğrenci Kullanıcı>", html, StringComparison.Ordinal);
        Assert.Contains("<Öğrenci Kullanıcı>", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.Contains("name=\"programOfferingId\" value=\"42\"", html, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.Contains("Güvenli çıkış", html, StringComparison.Ordinal);
        Assert.Contains("Öğrenci hesabı", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_and_student_navigation_are_separated_by_role()
    {
        using var host = CreateWebHost();
        var html = await RenderMainViewAsync(
            host.Services,
            "Home",
            "Index",
            new HomeViewModel(),
            AuthenticatedUser("Admin", "Yetkili Kullanıcı"));

        Assert.Contains("Yönetim paneli", html, StringComparison.Ordinal);
        Assert.Contains("İlanlar ve başvurular", html, StringComparison.Ordinal);
        Assert.Contains("Akademik katalog", html, StringComparison.Ordinal);
        Assert.Contains("Öğrenciler", html, StringComparison.Ordinal);
        Assert.Contains("Yönetici hesapları", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Sınav sonuçlarım", html, StringComparison.Ordinal);
        Assert.Contains("Yönetici hesabı", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Account_views_render_stable_field_names_antiforgery_and_distinct_admin_context()
    {
        using var host = CreateWebHost();
        var studentLogin = await RenderMainViewAsync(
            host.Services,
            "Account",
            "Login",
            new LoginViewModel { AccountType = LoginAccountType.Student });
        var adminLogin = await RenderMainViewAsync(
            host.Services,
            "Account",
            "AdminLogin",
            new LoginViewModel { AccountType = LoginAccountType.Admin },
            viewName: "Login");
        var register = await RenderMainViewAsync(
            host.Services,
            "Account",
            "Register",
            new RegisterViewModel());

        foreach (var fieldName in new[] { "ReturnUrl", "Username", "Password", "RememberMe" })
        {
            Assert.Contains($"name=\"{fieldName}\"", studentLogin, StringComparison.Ordinal);
        }

        Assert.Contains("__RequestVerificationToken", studentLogin, StringComparison.Ordinal);
        Assert.Contains("auth-shell--student", studentLogin, StringComparison.Ordinal);
        Assert.Contains("auth-shell--admin", adminLogin, StringComparison.Ordinal);
        Assert.Contains(
            "yalnızca yetkilendirilmiş",
            WebUtility.HtmlDecode(adminLogin),
            StringComparison.Ordinal);
        Assert.Contains("name=\"Username\"", adminLogin, StringComparison.Ordinal);
        Assert.Contains("name=\"Password\"", adminLogin, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", adminLogin, StringComparison.Ordinal);

        foreach (var fieldName in new[]
                 {
                     "Tc",
                     "FirstName",
                     "LastName",
                     "FatherName",
                     "BirthDate",
                     "Email",
                     "Telephone",
                     "Password",
                     "ConfirmPassword"
                 })
        {
            Assert.Contains($"name=\"{fieldName}\"", register, StringComparison.Ordinal);
        }

        Assert.Contains("__RequestVerificationToken", register, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Application_detail_hides_unpublished_result_and_preserves_secure_document_forms()
    {
        using var host = CreateWebHost();
        var applicationPublicId = Guid.Parse("457EA57D-6F18-4B19-87FB-86C4BC9FBB3A");
        var requirementPublicId = Guid.Parse("8B26C8C0-03A7-41DB-A7FA-AFF66B4A3F72");
        var html = await RenderMainViewAsync(
            host.Services,
            "Panel",
            "ApplicationDetail",
            new StudentApplicationDetailViewModel
            {
                PublicId = applicationPublicId,
                InstituteName = "Fen Bilimleri Enstitüsü",
                ProgramName = "Biyoteknoloji",
                AcademicYear = "2026–2027",
                TermName = "Güz",
                CurrentStatus = ApplicationStatus.Draft,
                UsesEvaluationWorkflow = true,
                UsesDocumentWorkflow = true,
                PublishedEvaluation = null,
                DocumentRequirements =
                [
                    new ApplicationDocumentRequirementViewModel
                    {
                        PublicId = requirementPublicId,
                        DisplayName = "Transkript",
                        IsRequired = true,
                        AllowedContentCategory = DocumentContentCategory.PdfOnly,
                        MaximumBytes = 5 * 1024 * 1024
                    }
                ],
                MissingRequiredDocuments = ["Transkript"]
            },
            AuthenticatedUser("Student", "Test Öğrenci"));

        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains(
            "puan, sıralama ve karar gösterilmez",
            decoded,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Yayımlanmış değerlendirme sonucu", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("Toplam puan", decoded, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"step\"", html, StringComparison.Ordinal);
        Assert.Contains("enctype=\"multipart/form-data\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"file\"", html, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.Contains("Başvuruyu gönder", decoded, StringComparison.Ordinal);
        Assert.Contains("disabled=\"disabled\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Published_result_renders_hierarchy_quota_snapshot_and_encoded_components()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var ambientCulture = CultureInfo.GetCultureInfo("en-US");

        try
        {
            CultureInfo.CurrentCulture = ambientCulture;
            CultureInfo.CurrentUICulture = ambientCulture;

            using var host = CreateWebHost();
            var html = await RenderMainViewAsync(
                host.Services,
                "Panel",
                "ApplicationDetail",
                new StudentApplicationDetailViewModel
                {
                    PublicId = Guid.Parse("0A3D7446-50D0-4709-A597-792180A66958"),
                    InstituteName = "Sosyal Bilimler Enstitüsü",
                    ProgramName = "<img src=x onerror=alert(1)>",
                    AcademicYear = "2026–2027",
                    TermName = "Bahar",
                    Quota = 7,
                    CurrentStatus = ApplicationStatus.Approved,
                    UsesEvaluationWorkflow = true,
                    PublishedEvaluation = new PublishedApplicationEvaluationViewModel
                    {
                        Outcome = EvaluationOutcome.Admitted,
                        TotalScore = 88.1250m,
                        Rank = 3,
                        ResultsPublishedAtUtc = new DateTime(
                            2026,
                            7,
                            24,
                            9,
                            0,
                            0,
                            DateTimeKind.Utc),
                        Components =
                        [
                            new PublishedEvaluationComponentViewModel
                            {
                                DisplayName = "<script>alert('criterion')</script>",
                                RawScore = 90.5m,
                                NormalizedScore = 90.25m,
                                WeightBasisPoints = 5000,
                                WeightedScore = 45.125m
                            }
                        ]
                    }
                },
                AuthenticatedUser("Student", "Test Öğrenci"));

            Assert.Equal(ambientCulture, CultureInfo.CurrentCulture);
            Assert.Equal(ambientCulture, CultureInfo.CurrentUICulture);

            var decoded = WebUtility.HtmlDecode(html);
            Assert.Contains("Yayımlanmış değerlendirme sonucu", decoded, StringComparison.Ordinal);
            Assert.Contains("Toplam puan", decoded, StringComparison.Ordinal);
            Assert.Contains("88,1250", decoded, StringComparison.Ordinal);
            Assert.Contains(">90,5000<", html, StringComparison.Ordinal);
            Assert.Contains(">90,2500<", html, StringComparison.Ordinal);
            Assert.Contains(">45,1250<", html, StringComparison.Ordinal);
            Assert.Contains("Sıralama", decoded, StringComparison.Ordinal);
            Assert.Contains("Kontenjan", decoded, StringComparison.Ordinal);
            Assert.Contains(">7<", html, StringComparison.Ordinal);
            Assert.Contains("snapshot verilerine dayanır", decoded, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>alert", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<img src=x", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
            Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void Accessibility_styles_define_visible_focus_and_reduced_motion_behavior()
    {
        var css = File.ReadAllText(
            Path.Combine(
                RepositoryRoot(),
                "GraduateApp.Web",
                "wwwroot",
                "css",
                "site.css"));

        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
        Assert.Contains("--cu-focus-ring", css, StringComparison.Ordinal);
    }

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
                        .AddApplicationPart(typeof(HomeController).Assembly);
                })
                .Configure(_ => { }))
            .Build();

    private static ClaimsPrincipal AuthenticatedUser(string role, string name) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, name),
                    new Claim(ClaimTypes.Role, role)
                ],
                "GraduateUiFoundationTest"));

    private static async Task<string> RenderMainViewAsync<TModel>(
        IServiceProvider services,
        string controller,
        string action,
        TModel model,
        ClaimsPrincipal? user = null,
        string? viewName = null)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var renderCulture = CultureInfo.GetCultureInfo("tr-TR");

        try
        {
            CultureInfo.CurrentCulture = renderCulture;
            CultureInfo.CurrentUICulture = renderCulture;

            using var scope = services.CreateScope();
            var scopedServices = scope.ServiceProvider;
            var httpContext = new DefaultHttpContext
            {
                RequestServices = scopedServices,
                User = user ?? new ClaimsPrincipal(new ClaimsIdentity())
            };
            httpContext.Features.Set<IRequestCultureFeature>(
                new RequestCultureFeature(new RequestCulture(renderCulture), provider: null));
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost");
            var routeData = new RouteData();
            routeData.Values["controller"] = controller;
            routeData.Values["action"] = action;
            routeData.Routers.Add(new TestRouter());
            var actionContext = new ActionContext(
                httpContext,
                routeData,
                new ActionDescriptor(),
                new ModelStateDictionary());
            var viewEngine = scopedServices.GetRequiredService<ICompositeViewEngine>();
            var viewResult = viewEngine.FindView(
                actionContext,
                viewName ?? action,
                isMainPage: true);
            Assert.True(
                viewResult.Success,
                $"{controller}/{viewName ?? action} view bulunamadı: "
                + string.Join(", ", viewResult.SearchedLocations ?? []));
            var viewData = new ViewDataDictionary<TModel>(
                scopedServices.GetRequiredService<IModelMetadataProvider>(),
                actionContext.ModelState)
            {
                Model = model
            };
            var tempData = new TempDataDictionary(
                httpContext,
                scopedServices.GetRequiredService<ITempDataProvider>());
            using var writer = new StringWriter(renderCulture);
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
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static string OpeningTagForLinkText(string html, string text)
    {
        var textIndex = html.IndexOf($">{text}</a>", StringComparison.Ordinal);
        Assert.True(textIndex >= 0, $"Bağlantı metni bulunamadı: {text}");
        var tagStart = html.LastIndexOf("<a", textIndex, StringComparison.Ordinal);
        var tagEnd = html.IndexOf('>', tagStart);
        Assert.True(tagStart >= 0 && tagEnd > tagStart);
        return html[tagStart..(tagEnd + 1)];
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private sealed class TestRouter : IRouter
    {
        public Task RouteAsync(RouteContext context) => Task.CompletedTask;

        public VirtualPathData GetVirtualPath(VirtualPathContext context) =>
            new(this, $"/{context.Values["controller"]}/{context.Values["action"]}");
    }
}
