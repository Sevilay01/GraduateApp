using GraduateApp.API.Domain;

namespace GraduateApp.Tests;

public sealed class ProgramNameEnglishCatalogTests
{
    [Theory]
    [InlineData("Bilgisayar Mühendisliği", "Computer Engineering")]
    [InlineData("  bilgisayar   mühendisliği  ", "Computer Engineering")]
    [InlineData("Elektrik-Elektronik Mühendisliği", "Electrical and Electronics Engineering")]
    [InlineData("İş Sağlığı ve Güvenliği", "Occupational Health and Safety")]
    [InlineData("Rehberlik ve Psikolojik Danışmanlık", "Guidance and Psychological Counseling")]
    [InlineData("Adli Bilimler (Doktora)", "Forensic Sciences")]
    [InlineData("Adli Bilimler (Tezli YL)", "Forensic Sciences")]
    [InlineData("İşletme (Tezsiz YL)", "Business Administration")]
    [InlineData("İşletme (Uzaktan Tezsiz YL)", "Business Administration")]
    [InlineData("  ADLİ   BİLİMLER  (  doktora  ) ", "Forensic Sciences")]
    [InlineData("Bağımlılık", "Addiction Studies")]
    [InlineData("İngiliz Dili Eğitimi", "English Language Education")]
    [InlineData("Sağlık Fiziği", "Health Physics")]
    [InlineData("Translasyonel Tıp İngilizce", "Translational Medicine (English)")]
    [InlineData("Yapay Zeka ve Veri Mühendisliği", "Artificial Intelligence and Data Engineering")]
    public void Known_program_names_are_mapped_deterministically(
        string turkishName,
        string expectedEnglishName)
    {
        var mapped = ProgramNameEnglishCatalog.TryGetEnglishName(
            turkishName,
            out var englishName);

        Assert.True(mapped);
        Assert.Equal(expectedEnglishName, englishName);
    }

    [Fact]
    public void Unknown_program_name_is_not_guessed()
    {
        var mapped = ProgramNameEnglishCatalog.TryGetEnglishName(
            "Kuruma Özgü Disiplinlerarası Program",
            out var englishName);

        Assert.False(mapped);
        Assert.Empty(englishName);
    }

    [Fact]
    public void Unknown_parenthesized_suffix_is_preserved_and_not_mapped()
    {
        const string programName = "Adli Bilimler (Sertifika)";

        var mapped = ProgramNameEnglishCatalog.TryGetEnglishName(
            programName,
            out var englishName);

        Assert.Equal(programName, ProgramNameEnglishCatalog.Normalize(programName));
        Assert.False(mapped);
        Assert.Empty(englishName);
    }
}
