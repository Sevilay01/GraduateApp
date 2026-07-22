using System.ComponentModel.DataAnnotations;

namespace GraduateApp.Web.Models;

public enum ApplicationStatus
{
    [Display(Name = "Taslak")]
    Draft,

    [Display(Name = "Beklemede")]
    Pending,

    [Display(Name = "İnceleniyor")]
    UnderReview,

    [Display(Name = "Onaylandı")]
    Approved,

    [Display(Name = "Reddedildi")]
    Rejected,

    [Display(Name = "Geri çekildi")]
    Withdrawn
}

public static class ApplicationStatusExtensions
{
    public static string DisplayName(this ApplicationStatus status) => status switch
    {
        ApplicationStatus.Draft => "Taslak",
        ApplicationStatus.Pending => "Beklemede",
        ApplicationStatus.UnderReview => "İnceleniyor",
        ApplicationStatus.Approved => "Onaylandı",
        ApplicationStatus.Rejected => "Reddedildi",
        ApplicationStatus.Withdrawn => "Geri çekildi",
        _ => "Bilinmiyor"
    };

    public static IReadOnlyList<ApplicationStatus> AllowedAdminTransitions(this ApplicationStatus status) => status switch
    {
        ApplicationStatus.Draft => [],
        ApplicationStatus.Pending => [ApplicationStatus.UnderReview],
        ApplicationStatus.UnderReview => [ApplicationStatus.Approved, ApplicationStatus.Rejected],
        _ => []
    };
}
