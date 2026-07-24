using GraduateApp.API.Domain;
using GraduateApp.API.Models;

namespace GraduateApp.API.Services;

internal static class EvaluationPolicyInvariant
{
    public static EvaluationPolicyInvariantResult Validate(
        IEnumerable<ProgramOfferingEvaluationCriterion> criteria,
        IEnumerable<int> requiredExamIds)
    {
        var items = criteria.ToArray();
        if (items.Length == 0
            || items.Sum(item => item.WeightBasisPoints) != EvaluationScoring.TotalWeightBasisPoints
            || items.Any(item => item.WeightBasisPoints <= 0
                || item.MaximumRawScore <= 0m
                || item.TieBreakPriority <= 0)
            || items.Select(item => item.NormalizedCode).Distinct(StringComparer.Ordinal).Count() != items.Length
            || items.Select(item => item.TieBreakPriority).Distinct().Count() != items.Length)
        {
            return new(EvaluationPolicyInvariantFailure.InvalidPolicy, null, null);
        }

        var examCriteria = items
            .Where(item => item.SourceType == EvaluationCriterionSourceType.ExamScore)
            .ToArray();
        if (examCriteria.FirstOrDefault(item => !item.ExamId.HasValue) is not null)
        {
            return new(EvaluationPolicyInvariantFailure.MissingExamSelection, null, null);
        }

        var requiredExamIdSet = requiredExamIds.ToHashSet();
        var orphanCriterion = examCriteria.FirstOrDefault(item => !requiredExamIdSet.Contains(item.ExamId!.Value));
        return orphanCriterion is null
            ? EvaluationPolicyInvariantResult.Valid
            : new(
                EvaluationPolicyInvariantFailure.MissingRequiredExamRequirement,
                orphanCriterion.ExamId,
                orphanCriterion.Exam?.ExamName);
    }

    public static string OpeningError(EvaluationPolicyInvariantResult result) => result.Failure switch
    {
        EvaluationPolicyInvariantFailure.InvalidPolicy =>
            "İlan açılmadan önce toplam ağırlığı 10000 basis point olan geçerli bir değerlendirme politikası tanımlayın.",
        EvaluationPolicyInvariantFailure.MissingExamSelection =>
            "İlan açılamaz: Sınav puanı değerlendirme kriteri için bir sınav seçilmelidir.",
        EvaluationPolicyInvariantFailure.MissingRequiredExamRequirement =>
            $"İlan açılamaz: {SafeExamName(result.ExamName)} değerlendirme kriteri için zorunlu sınav koşulu bulunmalıdır.",
        _ => throw new InvalidOperationException("Geçerli değerlendirme politikası için hata mesajı üretilemez.")
    };

    public static string DraftCreationError(EvaluationPolicyInvariantResult result) => result.Failure switch
    {
        EvaluationPolicyInvariantFailure.InvalidPolicy =>
            "Taslak oluşturulamaz: İlanın değerlendirme politikası geçerli değildir. Lütfen ilan yöneticisiyle iletişime geçin.",
        EvaluationPolicyInvariantFailure.MissingExamSelection =>
            "Taslak oluşturulamaz: İlanın sınav puanı değerlendirme kriteri için geçerli sınav seçimi bulunmamaktadır. Lütfen ilan yöneticisiyle iletişime geçin.",
        EvaluationPolicyInvariantFailure.MissingRequiredExamRequirement =>
            $"Taslak oluşturulamaz: İlanın {SafeExamName(result.ExamName)} değerlendirme kriteri için zorunlu sınav koşulu bulunmalıdır. Lütfen ilan yöneticisiyle iletişime geçin.",
        _ => throw new InvalidOperationException("Geçerli değerlendirme politikası için hata mesajı üretilemez.")
    };

    private static string SafeExamName(string? examName) =>
        string.IsNullOrWhiteSpace(examName) ? "seçili sınav" : examName;
}

internal sealed record EvaluationPolicyInvariantResult(
    EvaluationPolicyInvariantFailure Failure,
    int? ExamId,
    string? ExamName)
{
    public static EvaluationPolicyInvariantResult Valid { get; } =
        new(EvaluationPolicyInvariantFailure.None, null, null);

    public bool IsValid => Failure == EvaluationPolicyInvariantFailure.None;
}

internal enum EvaluationPolicyInvariantFailure
{
    None,
    InvalidPolicy,
    MissingExamSelection,
    MissingRequiredExamRequirement
}
