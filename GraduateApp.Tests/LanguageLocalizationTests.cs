using GraduateApp.Web.Controllers;
using GraduateApp.Web.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
        Assert.Equal(UiText.TurkishCultureName, UiText.CurrentCultureName(httpContext));

        httpContext.Features.Set<IRequestCultureFeature>(
            new RequestCultureFeature(
                new RequestCulture(UiText.EnglishCultureName),
                new CookieRequestCultureProvider()));

        Assert.Equal("Home", UiText.Get(httpContext, "Navigation.Home"));
        Assert.Equal(UiText.EnglishCultureName, UiText.CurrentCultureName(httpContext));
    }

    private static (LanguageController Controller, DefaultHttpContext HttpContext) CreateController()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost");
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());
        var controller = new LanguageController
        {
            ControllerContext = new ControllerContext(actionContext),
            Url = new UrlHelper(actionContext)
        };
        return (controller, httpContext);
    }
}
