namespace GraduateApp.API.Services;

internal static class ExamValidityPolicy
{
    public const int AlesValidityYears = 5;

    private static readonly Lazy<TimeZoneInfo> IstanbulTimeZone = new(ResolveIstanbulTimeZone);

    public static bool IsAles(string examName)
    {
        var normalized = examName.Trim();
        return normalized.Equals("ALES", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("ALES ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("ALES-", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("ALES/", StringComparison.OrdinalIgnoreCase);
    }

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
