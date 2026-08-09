using GraduateApp.Web.Controllers;
using GraduateApp.Web.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace GraduateApp.Tests;

public sealed class LanguageLocalizationTests
{
    [Theory]
    [InlineData(UiText.TurkishCultureName)]
    [InlineData(UiText.EnglishCultureName)]
    public void Supported_language_selection_writes_secure_cookie_and_preserves_local_return_url(
        string culture)
    {
        var (controller, httpContext) = CreateController();

        var result = Assert.IsType<LocalRedirectResult>(
            controller.Set(culture, "/Panel?search=test"));

        Assert.Equal("/Panel?search=test", result.Url);
        var setCookie = Uri.UnescapeDataString(httpContext.Response.Headers.SetCookie.ToString());
        Assert.Contains(CookieRequestCultureProvider.DefaultCookieName, setCookie, StringComparison.Ordinal);
        Assert.Contains($"c={culture}|uic={culture}", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsupported_language_is_rejected_without_writing_a_cookie()
    {
        var (controller, httpContext) = CreateController();

        var result = Assert.IsType<BadRequestObjectResult>(
            controller.Set("de-DE", "/"));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.False(httpContext.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public void External_return_url_is_not_preserved()
    {
        var (controller, _) = CreateController();

        var result = Assert.IsType<RedirectToActionResult>(
            controller.Set(UiText.EnglishCultureName, "https://evil.example.test/"));

        Assert.Equal("Index", result.ActionName);
        Assert.Equal("Home", result.ControllerName);
    }

    [Fact]
    public void Ui_text_defaults_to_turkish_and_uses_explicit_request_culture()
    {
        var httpContext = new DefaultHttpContext();

        Assert.Equal("Ana sayfa", UiText.Get(httpContext, "Navigation.Home"));
        Assert.Equal("Yönetici girişi", UiText.Get(httpContext, "Login.AdminTitle"));
        Assert.Equal("Öğrenci girişi", UiText.Get(httpContext, "Login.StudentTitle"));
        Assert.Equal("Farklı hesapla giriş yap", UiText.Get(httpContext, "Login.SwitchAccount"));
        Assert.Equal(UiText.TurkishCultureName, UiText.CurrentCultureName(httpContext));

        httpContext.Features.Set<IRequestCultureFeature>(
            new RequestCultureFeature(
                new RequestCulture(UiText.EnglishCultureName),
                new CookieRequestCultureProvider()));

        Assert.Equal("Home", UiText.Get(httpContext, "Navigation.Home"));
        Assert.Equal(UiText.EnglishCultureName, UiText.CurrentCultureName(httpContext));
        Assert.Equal(
            "Computer Engineering",
            UiText.SelectLocalized(
                httpContext,
                "Bilgisayar Mühendisliği",
                "Computer Engineering"));
        Assert.Equal(
            "Bilgisayar Mühendisliği",
            UiText.SelectLocalized(
                httpContext,
                "Bilgisayar Mühendisliği",
                null));
    }

    [Fact]
    public void Turkish_and_English_resource_catalogs_have_identical_keys()
    {
        var manager = new System.Resources.ResourceManager(
            "GraduateApp.Web.Resources.SharedText",
            typeof(UiText).Assembly);
        var turkish = manager.GetResourceSet(
            System.Globalization.CultureInfo.InvariantCulture,
            createIfNotExists: true,
            tryParents: false);
        var english = manager.GetResourceSet(
            System.Globalization.CultureInfo.GetCultureInfo(UiText.EnglishCultureName),
            createIfNotExists: true,
            tryParents: false);

        Assert.NotNull(turkish);
        Assert.NotNull(english);

        static string[] Keys(System.Resources.ResourceSet resources) =>
            resources.Cast<System.Collections.DictionaryEntry>()
                .Select(item => Assert.IsType<string>(item.Key))
                .Order(StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(Keys(turkish!), Keys(english!));
    }

    [Fact]
    public void English_request_localizes_evaluation_summaries_and_blockers()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IRequestCultureFeature>(
            new RequestCultureFeature(
                new RequestCulture(UiText.EnglishCultureName),
                new CookieRequestCultureProvider()));

        Assert.Equal(
            "2/3 required documents approved",
            UiText.LocalizeDocumentReviewSummary(httpContext, "2/3 zorunlu belge onaylı"));
        Assert.Equal(
            "The offering must be closed first.",
            UiText.LocalizeEvaluationBlockingReason(httpContext, "İlan önce kapatılmalıdır."));
        Assert.Equal(
            "Application 00000000-0000-0000-0000-000000000001 has missing criteria: GPA.",
            UiText.LocalizeEvaluationBlockingReason(
                httpContext,
                "00000000-0000-0000-0000-000000000001 başvurusunda eksik kriterler: GPA."));
    }

    [Theory]
    [InlineData(UiText.TurkishCultureName, "Doktora", "Doktora")]
    [InlineData(UiText.TurkishCultureName, "Tezli Yüksek Lisans", "Tezli Yüksek Lisans")]
    [InlineData(UiText.TurkishCultureName, "Tezsiz Yüksek Lisans", "Tezsiz Yüksek Lisans")]
    [InlineData(UiText.TurkishCultureName, "Uzaktan Tezsiz Yüksek Lisans", "Uzaktan Tezsiz Yüksek Lisans")]
    [InlineData(UiText.EnglishCultureName, "Doktora", "Doctorate")]
    [InlineData(UiText.EnglishCultureName, "Tezli Yüksek Lisans", "Master's Degree with Thesis")]
    [InlineData(UiText.EnglishCultureName, "Tezsiz Yüksek Lisans", "Non-Thesis Master's Degree")]
    [InlineData(UiText.EnglishCultureName, "Uzaktan Tezsiz Yüksek Lisans", "Distance Non-Thesis Master's Degree")]
    public void Degree_types_localize_from_the_canonical_turkish_value(
        string culture,
        string canonicalValue,
        string expected)
    {
        var httpContext = CreateContext(culture);

        Assert.Equal(expected, UiText.LocalizeDegreeType(httpContext, canonicalValue));
    }

    [Fact]
    public void Unknown_degree_type_uses_the_canonical_value_without_fallback_leaking()
    {
        var httpContext = CreateContext(UiText.EnglishCultureName);

        Assert.Equal("Yeni Derece", UiText.LocalizeDegreeType(httpContext, "Yeni Derece"));
    }

    [Fact]
    public void Degree_type_localization_does_not_leak_between_cultures()
    {
        var english = CreateContext(UiText.EnglishCultureName);
        var turkish = CreateContext(UiText.TurkishCultureName);

        Assert.Equal("Doctorate", UiText.LocalizeDegreeType(english, "Doktora"));
        Assert.Equal("Doktora", UiText.LocalizeDegreeType(turkish, "Doktora"));
        Assert.Equal("Doctorate", UiText.LocalizeDegreeType(english, "Doktora"));
        Assert.Equal("Doktora", UiText.LocalizeDegreeType(turkish, "Doktora"));
    }

    [Fact]
    public void English_program_names_remain_unmodified_while_degree_types_localize_separately()
    {
        var httpContext = CreateContext(UiText.EnglishCultureName);

        Assert.Equal(
            "Computer Engineering",
            UiText.SelectLocalized(httpContext, "Bilgisayar Mühendisliği", "Computer Engineering"));
        Assert.Equal("Doctorate", UiText.LocalizeDegreeType(httpContext, "Doktora"));
        Assert.NotEqual(
            UiText.SelectLocalized(httpContext, "Bilgisayar Mühendisliği", "Computer Engineering"),
            UiText.LocalizeDegreeType(httpContext, "Doktora"));
    }

    private static (LanguageController Controller, DefaultHttpContext HttpContext) CreateController()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost");
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ControllerActionDescriptor(),
            new ModelStateDictionary());
        var controller = new LanguageController
        {
            ControllerContext = new ControllerContext(actionContext),
            Url = new UrlHelper(actionContext)
        };
        return (controller, httpContext);
    }

    private static DefaultHttpContext CreateContext(string cultureName)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IRequestCultureFeature>(
            new RequestCultureFeature(
                new RequestCulture(cultureName),
                new CookieRequestCultureProvider()));
        return httpContext;
    }
}
