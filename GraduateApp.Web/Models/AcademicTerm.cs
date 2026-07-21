using System.ComponentModel.DataAnnotations;

namespace GraduateApp.Web.Models;

public enum AcademicTerm
{
    [Display(Name = "Belirtilmemiş")]
    LegacyUnspecified = 0,

    [Display(Name = "Güz")]
    Fall = 1,

    [Display(Name = "Bahar")]
    Spring = 2,

    [Display(Name = "Yaz")]
    Summer = 3
}

public static class AcademicTermDisplayExtensions
{
    public static IReadOnlyList<AcademicTerm> OfferingValues { get; } =
        [AcademicTerm.Fall, AcademicTerm.Spring, AcademicTerm.Summer];

    public static string DisplayName(this AcademicTerm term) => term switch
    {
        AcademicTerm.Fall => "Güz",
        AcademicTerm.Spring => "Bahar",
        AcademicTerm.Summer => "Yaz",
        _ => "Belirtilmemiş"
    };
}
