namespace GraduateApp.Web.Models;

public enum AcademicTerm
{
    LegacyUnspecified = 0,
    Fall = 1,
    Spring = 2,
    Summer = 3
}

public static class AcademicTermDisplayExtensions
{
    public static string DisplayName(this AcademicTerm term) => term switch
    {
        AcademicTerm.Fall => "Güz",
        AcademicTerm.Spring => "Bahar",
        AcademicTerm.Summer => "Yaz",
        _ => "Belirtilmemiş"
    };
}
