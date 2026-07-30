using GraduateApp.Web.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Web.Controllers;

public sealed class LanguageController : Controller
{
    private static readonly HashSet<string> SupportedCultures =
        new(StringComparer.OrdinalIgnoreCase)
        {
            UiText.TurkishCultureName,
            UiText.EnglishCultureName
        };

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult Set(string culture, string? returnUrl = null)
    {
        if (!SupportedCultures.Contains(culture))
        {
            return BadRequest("Desteklenmeyen dil seçimi.");
        }

        var normalizedCulture = string.Equals(
            culture,
            UiText.EnglishCultureName,
            StringComparison.OrdinalIgnoreCase)
            ? UiText.EnglishCultureName
            : UiText.TurkishCultureName;
        var requestCulture = new RequestCulture(normalizedCulture);

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(requestCulture),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = true
            });

        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Home");
    }
}
