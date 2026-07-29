using GraduateApp.Web.Services;

namespace GraduateApp.Tests;

public sealed class UndergraduateProgramRecommendationTests
{
    [Theory]
    [InlineData("Bilgisayar Mühendisliği", "Bilgisayar Mühendisliği")]
    [InlineData("bilgisayar mühendisliği", "BİLGİSAYAR MÜHENDİSLİĞİ")]
    [InlineData("  Bilgisayar   Mühendisliği  ", "Bilgisayar Mühendisliği")]
    public void Exact_program_names_match_with_turkish_case_and_whitespace(
        string graduatedProgram,
        string offeringProgram)
    {
        Assert.True(
            UndergraduateProgramRecommendation.IsExactProgramNameMatch(
                graduatedProgram,
                offeringProgram));
    }

    [Theory]
    [InlineData(
        "Bilgisayar Mühendisliği",
        "Bilgisayar Mühendisliği (Tezli YL)",
        "Tezli Yüksek Lisans")]
    [InlineData(
        "bilgisayar mühendisliği",
        "BİLGİSAYAR MÜHENDİSLİĞİ ( TEZLİ Y.L. )",
        "Tezli Yüksek Lisans")]
    [InlineData("İşletme", "İşletme (Doktora)", "Doktora")]
    [InlineData("Müzik", "Müzik (Sanatta Yeterlik)", "Sanatta Yeterlik")]
    public void Matching_degree_suffix_does_not_hide_the_same_program_area(
        string graduatedProgram,
        string offeringProgram,
        string degreeType)
    {
        Assert.True(
            UndergraduateProgramRecommendation.IsProgramAreaMatch(
                graduatedProgram,
                offeringProgram,
                degreeType));
    }

    [Theory]
    [InlineData(
        "Bilgisayar",
        "Bilgisayar Mühendisliği (Tezli YL)",
        "Tezli Yüksek Lisans")]
    [InlineData(
        "Yazılım Mühendisliği",
        "Bilgisayar Mühendisliği (Tezli YL)",
        "Tezli Yüksek Lisans")]
    [InlineData(
        "Bilgisayar Mühendisliği",
        "Bilgisayar Mühendisliği (İngilizce)",
        "Tezli Yüksek Lisans")]
    [InlineData(
        "Bilgisayar Mühendisliği",
        "Bilgisayar Mühendisliği (Tezli YL)",
        "Doktora")]
    [InlineData("", "Bilgisayar Mühendisliği", "Tezli Yüksek Lisans")]
    [InlineData(null, "Bilgisayar Mühendisliği", "Tezli Yüksek Lisans")]
    [InlineData("Bilgisayar Mühendisliği", null, "Tezli Yüksek Lisans")]
    public void Different_meaning_or_inconsistent_degree_does_not_match(
        string? graduatedProgram,
        string? offeringProgram,
        string? degreeType)
    {
        Assert.False(
            UndergraduateProgramRecommendation.IsProgramAreaMatch(
                graduatedProgram,
                offeringProgram,
                degreeType));
    }

    [Theory]
    [InlineData("Bilgisayar Mühendisliği", "Yazılım Mühendisliği")]
    [InlineData("Bilgisayar", "Bilgisayar Mühendisliği")]
    [InlineData("", "Bilgisayar Mühendisliği")]
    [InlineData(null, "Bilgisayar Mühendisliği")]
    [InlineData("Bilgisayar Mühendisliği", null)]
    public void Different_or_missing_program_names_do_not_match_exactly(
        string? graduatedProgram,
        string? offeringProgram)
    {
        Assert.False(
            UndergraduateProgramRecommendation.IsExactProgramNameMatch(
                graduatedProgram,
                offeringProgram));
    }
}
