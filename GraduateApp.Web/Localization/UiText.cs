using System.Globalization;
using System.Resources;
using GraduateApp.Web.Models;
using Microsoft.AspNetCore.Localization;

namespace GraduateApp.Web.Localization;

public static class UiText
{
    public const string TurkishCultureName = "tr-TR";
    public const string EnglishCultureName = "en-US";

    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo(TurkishCultureName);
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo(EnglishCultureName);
    private static readonly ResourceManager Resources = new("GraduateApp.Web.Resources.SharedText", typeof(UiText).Assembly);
    private static readonly IReadOnlyDictionary<string, string> DegreeTypeResourceKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Doktora"] = "DegreeType.Doctorate",
            ["Tezli Yüksek Lisans"] = "DegreeType.ThesisMasters",
            ["Tezsiz Yüksek Lisans"] = "DegreeType.NonThesisMasters",
            ["Uzaktan Tezsiz Yüksek Lisans"] = "DegreeType.DistanceNonThesisMasters"
        };

    public static IReadOnlyList<CultureInfo> SupportedCultures { get; } = [TurkishCulture, EnglishCulture];

    public static string Get(HttpContext? context, string key) =>
        Resources.GetString(key, ResolveCulture(context)) ?? key;

    public static string GetCurrent(string key) =>
        Resources.GetString(key, ResolveCulture(CultureInfo.CurrentUICulture)) ?? key;

    public static string Format(HttpContext? context, string key, params object?[] arguments) =>
        string.Format(ResolveCulture(context), Get(context, key), arguments);

    public static string CurrentCultureName(HttpContext? context) =>
        ResolveCulture(context).Name;

    public static string SelectLocalized(
        HttpContext? context,
        string defaultValue,
        string? englishValue) =>
        CurrentCultureName(context) == EnglishCultureName && !string.IsNullOrWhiteSpace(englishValue)
            ? englishValue
            : defaultValue;

    public static string LocalizeDegreeType(HttpContext? context, string? degreeType) =>
        degreeType is null
            ? string.Empty
            : DegreeTypeResourceKeys.TryGetValue(degreeType.Trim(), out var key)
                ? Get(context, key)
                : degreeType;

    public static string LocalizeApplicationStatus(HttpContext? context, ApplicationStatus status) =>
        Get(context, $"Status.{status}");

    public static string LocalizeEvaluationOutcome(HttpContext? context, EvaluationOutcome outcome) =>
        Get(context, $"Outcome.{outcome}");

    public static string LocalizeDocumentReview(HttpContext? context, DocumentReviewStatus status) =>
        Get(context, $"DocumentReview.{status}");

    public static string LocalizeDocumentCategory(HttpContext? context, DocumentContentCategory category) =>
        Get(context, $"DocumentCategory.{category}");

    public static string LocalizeEvaluationState(HttpContext? context, OfferingEvaluationState state) =>
        Get(context, $"EvaluationState.{state}");

    public static string LocalizeCriterionSource(HttpContext? context, EvaluationCriterionSourceType sourceType) =>
        Get(context, $"CriterionSource.{sourceType}");

    public static string LocalizeEligibility(HttpContext? context, EvaluationEligibilityStatus status) =>
        Get(context, $"Eligibility.{status}");

    public static string LocalizeDocumentReviewSummary(HttpContext? context, string? summary)
    {
        if (string.Equals(summary, "Legacy belge akışı", StringComparison.Ordinal))
        {
            return Get(context, "Admin.Evaluation.DocumentReviewLegacy");
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            summary ?? string.Empty,
            @"^(?<approved>\d+)/(?<required>\d+) zorunlu belge onaylı$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success
            ? Format(
                context,
                "Admin.Evaluation.DocumentReviewProgress",
                match.Groups["approved"].Value,
                match.Groups["required"].Value)
            : summary ?? string.Empty;
    }

    public static string LocalizeEvaluationBlockingReason(HttpContext? context, string reason)
    {
        var key = reason switch
        {
            "İlan sonuçları zaten kesinleştirilmiş veya yayımlanmış." => "Admin.Evaluation.Blocker.AlreadyFinalized",
            "İlan önce kapatılmalıdır." => "Admin.Evaluation.Blocker.CloseOffering",
            "Son başvuru tarihi henüz geçmedi." => "Admin.Evaluation.Blocker.DeadlineNotPassed",
            "Kontenjan pozitif olmalıdır." => "Admin.Evaluation.Blocker.PositiveQuota",
            "Değerlendirme politikası geçerli ve 10000 basis point olmalıdır." => "Admin.Evaluation.Blocker.ValidPolicy",
            "Kesinleştirilecek gönderilmiş başvuru bulunmuyor." => "Admin.Evaluation.Blocker.NoSubmittedApplications",
            "İncelemeye alınmamış bekleyen başvurular var." => "Admin.Evaluation.Blocker.PendingApplications",
            "Tüm değerlendirilecek başvurular incelemede olmalıdır." => "Admin.Evaluation.Blocker.AllUnderReview",
            "Uygunluk kararı verilmemiş başvurular var." => "Admin.Evaluation.Blocker.EligibilityPending",
            "Uygun işaretlenmiş adayların tüm zorunlu güncel belgeleri onaylanmış olmalıdır." => "Admin.Evaluation.Blocker.DocumentsPending",
            _ => null
        };
        if (key is not null)
        {
            return Get(context, key);
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            reason,
            @"^(?<application>[0-9a-fA-F-]{36}) başvurusunda eksik kriterler: (?<criteria>.+)\.$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success
            ? Format(
                context,
                "Admin.Evaluation.Blocker.MissingCriteria",
                match.Groups["application"].Value,
                match.Groups["criteria"].Value)
            : reason;
    }

    public static string LocalizeAcademicTerm(HttpContext? context, string? termName) =>
        termName switch
        {
            "Güz" => Get(context, "AcademicTerm.Fall"),
            "Bahar" => Get(context, "AcademicTerm.Spring"),
            "Yaz" => Get(context, "AcademicTerm.Summer"),
            "Belirtilmemiş" => Get(context, "AcademicTerm.LegacyUnspecified"),
            _ => termName ?? string.Empty
        };

    private static CultureInfo ResolveCulture(CultureInfo? culture) =>
        string.Equals(culture?.Name, EnglishCultureName, StringComparison.OrdinalIgnoreCase)
            ? EnglishCulture
            : TurkishCulture;

    private static CultureInfo ResolveCulture(HttpContext? context)
    {
        var requestedCulture = context?
            .Features
            .Get<IRequestCultureFeature>()?
            .RequestCulture
            .UICulture;

        return requestedCulture is null ? TurkishCulture : ResolveCulture(requestedCulture);
    }
}
