using System.Globalization;
using System.Resources;
using GraduateApp.Web.Models;
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

    public static string LocalizeDegreeType(HttpContext context, string? degreeType) =>
        degreeType switch
        {
            "Doktora" => Get(context, "DegreeType.Doctorate"),
            "Tezli Yüksek Lisans" => Get(context, "DegreeType.ThesisMasters"),
            "Tezsiz Yüksek Lisans" => Get(context, "DegreeType.NonThesisMasters"),
            "Uzaktan Tezsiz Yüksek Lisans" => Get(context, "DegreeType.DistanceNonThesisMasters"),
            _ => degreeType ?? string.Empty
        };

    public static string LocalizeApplicationStatus(HttpContext context, ApplicationStatus status) =>
        Get(context, $"Status.{status}");

    public static string LocalizeEvaluationOutcome(HttpContext context, EvaluationOutcome outcome) =>
        Get(context, $"Outcome.{outcome}");

    public static string LocalizeDocumentReview(HttpContext context, DocumentReviewStatus status) =>
        Get(context, $"DocumentReview.{status}");

    public static string LocalizeDocumentCategory(HttpContext context, DocumentContentCategory category) =>
        Get(context, $"DocumentCategory.{category}");

    public static string LocalizeAcademicTerm(HttpContext context, string? termName) =>
        termName switch
        {
            "Güz" => Get(context, "AcademicTerm.Fall"),
            "Bahar" => Get(context, "AcademicTerm.Spring"),
            "Yaz" => Get(context, "AcademicTerm.Summer"),
            "Belirtilmemiş" => Get(context, "AcademicTerm.LegacyUnspecified"),
            _ => termName ?? string.Empty
        };

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
