using System.Globalization;
using System.Resources;
using Microsoft.AspNetCore.Localization;

namespace GraduateApp.Web.Localization;

public static class UiText
{
    public const string TurkishCultureName = "tr-TR";
    public const string EnglishCultureName = "en-US";

    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo(TurkishCultureName);
    private static readonly CultureInfo EnglishCulture =
        CultureInfo.GetCultureInfo(EnglishCultureName);
    private static readonly ResourceManager Resources =
        new("GraduateApp.Web.Resources.SharedText", typeof(UiText).Assembly);

    public static IReadOnlyList<CultureInfo> SupportedCultures { get; } =
        [TurkishCulture, EnglishCulture];

    public static string Get(HttpContext context, string key) =>
        Resources.GetString(key, ResolveCulture(context)) ?? key;

    public static string Format(HttpContext context, string key, params object?[] arguments) =>
        string.Format(ResolveCulture(context), Get(context, key), arguments);

    public static string CurrentCultureName(HttpContext context) =>
        ResolveCulture(context).Name;

    public static string SelectLocalized(
        HttpContext context,
        string defaultValue,
        string? englishValue) =>
        CurrentCultureName(context) == EnglishCultureName
        && !string.IsNullOrWhiteSpace(englishValue)
            ? englishValue
            : defaultValue;

    private static CultureInfo ResolveCulture(HttpContext context)
    {
        var requestedCulture = context.Features
            .Get<IRequestCultureFeature>()?
            .RequestCulture
            .UICulture;

        return string.Equals(
            requestedCulture?.Name,
            EnglishCultureName,
            StringComparison.OrdinalIgnoreCase)
            ? EnglishCulture
            : TurkishCulture;
    }
}
