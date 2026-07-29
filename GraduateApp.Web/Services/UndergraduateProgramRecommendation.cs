using System.Globalization;

namespace GraduateApp.Web.Services;

public static class UndergraduateProgramRecommendation
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    private static readonly CompareInfo TurkishCompare =
        TurkishCulture.CompareInfo;

    public static bool IsExactProgramNameMatch(
        string? graduatedProgram,
        string? offeringProgramName)
    {
        var graduated = NormalizeWhitespace(graduatedProgram);
        var offering = NormalizeWhitespace(offeringProgramName);
        return NamesMatch(graduated, offering);
    }

    public static bool IsProgramAreaMatch(
        string? graduatedProgram,
        string? offeringProgramName,
        string? offeringDegreeType)
    {
        var graduated = NormalizeWhitespace(graduatedProgram);
        var offering = NormalizeWhitespace(offeringProgramName);
        if (NamesMatch(graduated, offering))
        {
            return true;
        }

        var baseOfferingName = RemoveMatchingDegreeQualifier(
            offering,
            offeringDegreeType);
        return NamesMatch(graduated, baseOfferingName);
    }

    private static bool NamesMatch(string? left, string? right) =>
        left is not null
        && right is not null
        && TurkishCompare.Compare(
            left,
            right,
            CompareOptions.IgnoreCase) == 0;

    private static string? RemoveMatchingDegreeQualifier(
        string? offeringProgramName,
        string? offeringDegreeType)
    {
        if (offeringProgramName is null || !offeringProgramName.EndsWith(')'))
        {
            return null;
        }

        var qualifierStart = offeringProgramName.LastIndexOf('(');
        if (qualifierStart <= 0)
        {
            return null;
        }

        var qualifier = offeringProgramName[(qualifierStart + 1)..^1];
        var canonicalQualifier = CanonicalDegreeType(qualifier);
        var canonicalDegreeType = CanonicalDegreeType(offeringDegreeType);
        if (canonicalQualifier is null
            || canonicalDegreeType is null
            || !string.Equals(
                canonicalQualifier,
                canonicalDegreeType,
                StringComparison.Ordinal))
        {
            return null;
        }

        return NormalizeWhitespace(offeringProgramName[..qualifierStart]);
    }

    private static string? CanonicalDegreeType(string? value)
    {
        var normalized = NormalizeWhitespace(value);
        if (normalized is null)
        {
            return null;
        }

        var key = normalized
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .ToUpper(TurkishCulture);
        return key switch
        {
            "YL" or "YÜKSEK LİSANS" => "YÜKSEK LİSANS",
            "TEZLİ YL" or "TEZLİ YÜKSEK LİSANS" => "TEZLİ YÜKSEK LİSANS",
            "TEZSİZ YL" or "TEZSİZ YÜKSEK LİSANS" => "TEZSİZ YÜKSEK LİSANS",
            "DOKTORA" => "DOKTORA",
            "BÜTÜNLEŞİK DOKTORA" => "BÜTÜNLEŞİK DOKTORA",
            "SANATTA YETERLİK" => "SANATTA YETERLİK",
            _ => null
        };
    }

    private static string? NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
