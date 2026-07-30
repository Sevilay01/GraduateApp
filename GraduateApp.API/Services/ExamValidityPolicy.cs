namespace GraduateApp.API.Services;

internal static class ExamValidityPolicy
{
    public const int AlesValidityYears = 5;

    private static readonly Lazy<TimeZoneInfo> IstanbulTimeZone = new(ResolveIstanbulTimeZone);

    public static bool IsAles(string examName) =>
        NormalizeExamName(examName).Contains("ALES", StringComparison.Ordinal);

    public static bool UsesHundredPointScale(string examName)
    {
        var normalized = NormalizeExamName(examName);
        return normalized.Contains("ALES", StringComparison.Ordinal)
            || normalized.Contains("YDS", StringComparison.Ordinal)
            || normalized.Contains("YOKDIL", StringComparison.Ordinal);
    }

    private static string NormalizeExamName(string examName) =>
        examName.Trim().ToUpperInvariant()
            .Replace('Ö', 'O')
            .Replace('İ', 'I');

    public static DateOnly GetAlesEarliestAcceptedResultDate(DateTime applicationDeadlineUtc)
    {
        var utc = DateTime.SpecifyKind(applicationDeadlineUtc, DateTimeKind.Utc);
        var localDeadline = TimeZoneInfo.ConvertTimeFromUtc(utc, IstanbulTimeZone.Value);
        return DateOnly.FromDateTime(localDeadline).AddYears(-AlesValidityYears);
    }

    private static TimeZoneInfo ResolveIstanbulTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        }
    }
}
