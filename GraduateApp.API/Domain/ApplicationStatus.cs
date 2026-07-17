namespace GraduateApp.API.Domain;

public enum ApplicationStatus
{
    Pending,
    UnderReview,
    Approved,
    Rejected,
    Withdrawn
}

public static class ApplicationStatusRules
{
    private static readonly IReadOnlyDictionary<ApplicationStatus, ApplicationStatus[]> AllowedTransitions =
        new Dictionary<ApplicationStatus, ApplicationStatus[]>
        {
            [ApplicationStatus.Pending] = [ApplicationStatus.UnderReview],
            [ApplicationStatus.UnderReview] = [ApplicationStatus.Approved, ApplicationStatus.Rejected],
            [ApplicationStatus.Approved] = [],
            [ApplicationStatus.Rejected] = [],
            [ApplicationStatus.Withdrawn] = []
        };

    public static bool CanTransition(ApplicationStatus current, ApplicationStatus next) =>
        AllowedTransitions[current].Contains(next);

    public static bool TryParseStoredValue(string? value, out ApplicationStatus status)
    {
        if (Enum.TryParse(value, ignoreCase: true, out status))
        {
            return true;
        }

        status = value?.Trim() switch
        {
            "Sisteme Alındı" or "Onay Bekliyor" => ApplicationStatus.Pending,
            "İnceleniyor" => ApplicationStatus.UnderReview,
            "Onaylandı" => ApplicationStatus.Approved,
            "Reddedildi" => ApplicationStatus.Rejected,
            _ => default
        };

        return value?.Trim() is "Sisteme Alındı" or "Onay Bekliyor" or "İnceleniyor" or "Onaylandı" or "Reddedildi";
    }
}
