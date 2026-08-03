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
}
