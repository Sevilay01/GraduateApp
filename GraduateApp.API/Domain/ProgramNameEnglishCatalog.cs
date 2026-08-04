using System.Globalization;

namespace GraduateApp.API.Domain;

public static class ProgramNameEnglishCatalog
{
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly StringComparer TurkishNameComparer =
        StringComparer.Create(TurkishCulture, ignoreCase: true);

    private static readonly string[] DegreeSuffixes =
    [
        "Doktora",
        "Tezli YL",
        "Tezsiz YL",
        "Uzaktan Tezsiz YL"
    ];

    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(TurkishNameComparer)
        {
            ["Acil Yardım ve Afet Yönetimi"] = "Emergency and Disaster Management",
            ["Adli Bilimler"] = "Forensic Sciences",
            ["Anatomi"] = "Anatomy",
            ["Arkeoloji"] = "Archaeology",
            ["Bahçe Bitkileri"] = "Horticulture",
            ["Bağımlılık"] = "Addiction Studies",
            ["Beden Eğitimi ve Spor"] = "Physical Education and Sports",
            ["Beslenme ve Diyetetik"] = "Nutrition and Dietetics",
            ["Bilgisayar Bilimleri"] = "Computer Science",
            ["Bilgisayar Mühendisliği"] = "Computer Engineering",
            ["Bitki Koruma"] = "Plant Protection",
            ["Biyoloji"] = "Biology",
            ["Biyomedikal Mühendisliği"] = "Biomedical Engineering",
            ["Biyoteknoloji"] = "Biotechnology",
            ["Coğrafya"] = "Geography",
            ["Çevre Mühendisliği"] = "Environmental Engineering",
            ["Çocuk Gelişimi"] = "Child Development",
            ["Ebelik"] = "Midwifery",
            ["Eczacılık"] = "Pharmacy",
            ["Eğitim Bilimleri"] = "Educational Sciences",
            ["Ekonometri"] = "Econometrics",
            ["Ekonomi"] = "Economics",
            ["Elektrik-Elektronik Mühendisliği"] = "Electrical and Electronics Engineering",
            ["Endüstri Mühendisliği"] = "Industrial Engineering",
            ["Enerji Sistemleri Mühendisliği"] = "Energy Systems Engineering",
            ["Felsefe"] = "Philosophy",
            ["Fizik"] = "Physics",
            ["Fizyoloji"] = "Physiology",
            ["Gıda Mühendisliği"] = "Food Engineering",
            ["Halk Sağlığı"] = "Public Health",
            ["Hemşirelik"] = "Nursing",
            ["İletişim Bilimleri"] = "Communication Sciences",
            ["İngiliz Dili Eğitimi"] = "English Language Education",
            ["İnşaat Mühendisliği"] = "Civil Engineering",
            ["İş Sağlığı ve Güvenliği"] = "Occupational Health and Safety",
            ["İşletme"] = "Business Administration",
            ["İstatistik"] = "Statistics",
            ["Kamu Yönetimi"] = "Public Administration",
            ["Kimya"] = "Chemistry",
            ["Kimya Mühendisliği"] = "Chemical Engineering",
            ["Maden Mühendisliği"] = "Mining Engineering",
            ["Makine Mühendisliği"] = "Mechanical Engineering",
            ["Maliye"] = "Public Finance",
            ["Matematik"] = "Mathematics",
            ["Mimarlık"] = "Architecture",
            ["Moleküler Biyoloji ve Genetik"] = "Molecular Biology and Genetics",
            ["Otomotiv Mühendisliği"] = "Automotive Engineering",
            ["Peyzaj Mimarlığı"] = "Landscape Architecture",
            ["Psikoloji"] = "Psychology",
            ["Rehberlik ve Psikolojik Danışmanlık"] = "Guidance and Psychological Counseling",
            ["Sağlık Fiziği"] = "Health Physics",
            ["Sağlık Yönetimi"] = "Healthcare Management",
            ["Sanat ve Tasarım"] = "Art and Design",
            ["Siyaset Bilimi ve Kamu Yönetimi"] = "Political Science and Public Administration",
            ["Sosyal Hizmet"] = "Social Work",
            ["Sosyoloji"] = "Sociology",
            ["Su Ürünleri"] = "Fisheries",
            ["Şehir ve Bölge Planlama"] = "City and Regional Planning",
            ["Tarım Ekonomisi"] = "Agricultural Economics",
            ["Tarım Makineleri ve Teknolojileri Mühendisliği"] = "Agricultural Machinery and Technologies Engineering",
            ["Tarımsal Yapılar ve Sulama"] = "Agricultural Structures and Irrigation",
            ["Tarih"] = "History",
            ["Temel İslam Bilimleri"] = "Basic Islamic Sciences",
            ["Toprak Bilimi ve Bitki Besleme"] = "Soil Science and Plant Nutrition",
            ["Translasyonel Tıp İngilizce"] = "Translational Medicine (English)",
            ["Turizm İşletmeciliği"] = "Tourism Management",
            ["Türk Dili ve Edebiyatı"] = "Turkish Language and Literature",
            ["Türkçe Eğitimi"] = "Turkish Language Education",
            ["Uluslararası İlişkiler"] = "International Relations",
            ["Yabancı Diller Eğitimi"] = "Foreign Language Education",
            ["Yapay Zeka ve Veri Mühendisliği"] = "Artificial Intelligence and Data Engineering",
            ["Yönetim ve Organizasyon"] = "Management and Organization",
            ["Zootekni"] = "Animal Science"
        };

    public static bool TryGetEnglishName(string? programName, out string englishName)
    {
        englishName = string.Empty;
        if (string.IsNullOrWhiteSpace(programName))
        {
            return false;
        }

        if (!Names.TryGetValue(Normalize(programName), out var mappedName))
        {
            return false;
        }

        englishName = mappedName;
        return true;
    }

    public static string Normalize(string programName)
    {
        var normalized = CollapseWhitespace(programName);
        if (!normalized.EndsWith(')'))
        {
            return normalized;
        }

        var openingParenthesis = normalized.LastIndexOf('(');
        if (openingParenthesis <= 0)
        {
            return normalized;
        }

        var suffix = CollapseWhitespace(normalized[(openingParenthesis + 1)..^1]);
        if (!DegreeSuffixes.Any(candidate => TurkishNameComparer.Equals(candidate, suffix)))
        {
            return normalized;
        }

        return normalized[..openingParenthesis].TrimEnd();
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
