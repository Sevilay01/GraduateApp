using System.ComponentModel.DataAnnotations;

namespace GraduateApp.API.DTOs;

public sealed class StudentExamScoreInputDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir sınav seçiniz.")]
    public int ExamId { get; init; }

    [Required(ErrorMessage = "Sınav puanı zorunludur.")]
    [Range(
        typeof(decimal),
        "0",
        "999.99",
        ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Sınav puanı 0 ile 999,99 arasında geçerli bir ondalık sayı olmalıdır.")]
    public decimal? Score { get; init; }

    [Required(ErrorMessage = "Sonuç tarihi zorunludur.")]
    public DateOnly? ExamDate { get; init; }
}

public sealed record StudentExamScoreDto(
    int ScoreId,
    int ExamId,
    string ExamName,
    decimal Score,
    DateOnly? ExamDate);

public sealed record StudentExamCatalogItemDto(
    int ExamId,
    string ExamName,
    decimal MaximumScore);
