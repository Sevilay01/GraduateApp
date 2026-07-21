using System.Globalization;

namespace GraduateApp.API.Domain;

public static class DegreeTypeCatalog
{
    public const string Doctorate = "Doktora";
    public const string ThesisMasters = "Tezli Yüksek Lisans";
    public const string NonThesisMasters = "Tezsiz Yüksek Lisans";
    public const string DistanceNonThesisMasters = "Uzaktan Tezsiz Yüksek Lisans";

    private static readonly StringComparer TurkishComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

    public static IReadOnlyList<string> Values { get; } =
    [
        Doctorate,
        ThesisMasters,
        NonThesisMasters,
        DistanceNonThesisMasters
    ];

    public static bool TryCanonicalize(string? value, out string canonicalValue)
    {
        var trimmed = value?.Trim();
        canonicalValue = Values.FirstOrDefault(item => TurkishComparer.Equals(item, trimmed)) ?? string.Empty;
        return canonicalValue.Length > 0;
    }
}
