namespace GraduateApp.API.Domain;

public enum EvaluationCriterionSourceType
{
    UndergraduateGpa,
    ExamScore,
    ManualScore
}

public enum EvaluationEligibilityStatus
{
    Pending,
    Eligible,
    Ineligible
}

public enum EvaluationOutcome
{
    Admitted,
    NotAdmitted,
    Ineligible
}

public enum OfferingEvaluationState
{
    Configuring,
    Finalized,
    Published
}

public static class EvaluationScoring
{
    public const int TotalWeightBasisPoints = 10000;
    public const int StoredDecimalPlaces = 4;
    public const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    public static decimal Normalize(decimal rawScore, decimal maximumRawScore)
    {
        ValidateRawScore(rawScore, maximumRawScore);
        return Round(rawScore / maximumRawScore * 100m);
    }

    public static decimal Weight(decimal normalizedScore, int weightBasisPoints)
    {
        if (normalizedScore is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(normalizedScore));
        }

        if (weightBasisPoints is <= 0 or > TotalWeightBasisPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(weightBasisPoints));
        }

        return Round(normalizedScore * weightBasisPoints / TotalWeightBasisPoints);
    }

    public static EvaluationComponentScore CalculateComponent(
        decimal rawScore,
        decimal maximumRawScore,
        int weightBasisPoints)
    {
        ValidateRawScore(rawScore, maximumRawScore);
        if (weightBasisPoints is <= 0 or > TotalWeightBasisPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(weightBasisPoints));
        }

        var normalizedWithoutEarlyRounding = rawScore / maximumRawScore * 100m;
        var weightedWithoutEarlyRounding = normalizedWithoutEarlyRounding
            * weightBasisPoints
            / TotalWeightBasisPoints;
        return new EvaluationComponentScore(
            Round(normalizedWithoutEarlyRounding),
            Round(weightedWithoutEarlyRounding));
    }

    public static decimal Total(IEnumerable<decimal> weightedScores)
    {
        var total = Round(weightedScores.Sum());
        if (total is < 0m or > 100m)
        {
            throw new InvalidOperationException("Toplam değerlendirme puanı 0 ile 100 arasında olmalıdır.");
        }

        return total;
    }

    public static decimal Round(decimal value) =>
        Math.Round(value, StoredDecimalPlaces, Rounding);

    public static void ValidateRawScore(decimal rawScore, decimal maximumRawScore)
    {
        if (maximumRawScore <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRawScore));
        }

        if (rawScore < 0m || rawScore > maximumRawScore)
        {
            throw new ArgumentOutOfRangeException(nameof(rawScore));
        }
    }
}

public readonly record struct EvaluationComponentScore(
    decimal NormalizedScore,
    decimal WeightedScore);
