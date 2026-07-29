using System.Globalization;

namespace GraduateApp.Web.Services;

public static class UndergraduateProgramRecommendation
{
    private static readonly CompareInfo TurkishCompare =
        CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    public static bool IsExactProgramNameMatch(
        string? graduatedProgram,
        string? offeringProgramName)
    {
        var graduated = NormalizeWhitespace(graduatedProgram);
        var offering = NormalizeWhitespace(offeringProgramName);
        return graduated is not null
            && offering is not null
            && TurkishCompare.Compare(
                graduated,
                offering,
                CompareOptions.IgnoreCase) == 0;
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
