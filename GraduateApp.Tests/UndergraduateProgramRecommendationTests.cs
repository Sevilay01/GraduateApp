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
    [InlineData("Bilgisayar Mühendisliği", "Yazılım Mühendisliği")]
    [InlineData("Bilgisayar", "Bilgisayar Mühendisliği")]
    [InlineData("", "Bilgisayar Mühendisliği")]
    [InlineData(null, "Bilgisayar Mühendisliği")]
    [InlineData("Bilgisayar Mühendisliği", null)]
    public void Different_or_missing_program_names_do_not_match(
        string? graduatedProgram,
        string? offeringProgram)
    {
        Assert.False(
            UndergraduateProgramRecommendation.IsExactProgramNameMatch(
                graduatedProgram,
                offeringProgram));
    }
}
