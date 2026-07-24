using GraduateApp.API.Domain;
using System.Globalization;

namespace GraduateApp.Tests;

public sealed class EvaluationScoringTests
{
    [Theory]
    [InlineData("0", "4", "0")]
    [InlineData("2", "4", "50")]
    [InlineData("3.25", "4", "81.25")]
    [InlineData("4", "4", "100")]
    [InlineData("87.55555", "100", "87.5556")]
    public void Normalize_uses_decimal_math_and_four_decimal_places(
        string raw,
        string maximum,
        string expected)
    {
        Assert.Equal(Parse(expected), EvaluationScoring.Normalize(Parse(raw), Parse(maximum)));
    }

    [Theory]
    [InlineData("100", 10000, "100")]
    [InlineData("80", 2500, "20")]
    [InlineData("33.3333", 3333, "11.11")]
    [InlineData("12.34565", 5000, "6.1728")]
    public void Weight_uses_basis_points_and_explicit_midpoint_rounding(
        string normalized,
        int basisPoints,
        string expected)
    {
        Assert.Equal(Parse(expected), EvaluationScoring.Weight(Parse(normalized), basisPoints));
    }

    [Fact]
    public void Round_uses_away_from_zero_midpoint_rule()
    {
        Assert.Equal(1.2346m, EvaluationScoring.Round(1.23455m));
        Assert.Equal(-1.2346m, EvaluationScoring.Round(-1.23455m));
    }

    [Fact]
    public void Component_calculation_does_not_use_rounded_normalized_value_as_an_intermediate()
    {
        const decimal raw = 1.234567m;
        const decimal maximum = 7m;
        const int weight = 4321;

        var score = EvaluationScoring.CalculateComponent(raw, maximum, weight);

        Assert.Equal(EvaluationScoring.Round(raw / maximum * 100m), score.NormalizedScore);
        Assert.Equal(EvaluationScoring.Round(raw / maximum * 100m * weight / 10000m), score.WeightedScore);
    }

    [Theory]
    [InlineData("-0.0001", "100")]
    [InlineData("100.0001", "100")]
    [InlineData("1", "0")]
    public void Normalize_rejects_out_of_range_or_invalid_maximum(string raw, string maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EvaluationScoring.Normalize(Parse(raw), Parse(maximum)));
    }

    [Fact]
    public void Total_sums_rounded_decimal_components()
    {
        Assert.Equal(100m, EvaluationScoring.Total([25m, 25m, 50m]));
    }

    [Fact]
    public void Total_rejects_out_of_range_aggregate()
    {
        Assert.Throws<InvalidOperationException>(() => EvaluationScoring.Total([60m, 41m]));
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
