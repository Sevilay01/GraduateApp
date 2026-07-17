namespace GraduateApp.API.Domain;

public enum AcademicTerm
{
    LegacyUnspecified = 0,
    Fall = 1,
    Spring = 2,
    Summer = 3
}

public static class AcademicPeriodFormatter
{
    public static string FormatAcademicYear(int academicYearStart) =>
        academicYearStart > 0 ? $"{academicYearStart}–{academicYearStart + 1}" : "Belirtilmemiş";

    public static string FormatTerm(AcademicTerm term) => term switch
    {
        AcademicTerm.Fall => "Güz",
        AcademicTerm.Spring => "Bahar",
        AcademicTerm.Summer => "Yaz",
        _ => "Belirtilmemiş"
    };
}
