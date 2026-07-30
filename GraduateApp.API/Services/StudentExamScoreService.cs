using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Services;

public interface IStudentExamScoreService
{
    Task<IReadOnlyList<StudentExamScoreDto>> GetForStudentAsync(string studentTc, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudentExamCatalogItemDto>> GetCatalogAsync(CancellationToken cancellationToken);
    Task<ServiceResult<StudentExamScoreDto>> CreateAsync(
        string studentTc,
        StudentExamScoreInputDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult<StudentExamScoreDto>> UpdateAsync(
        string studentTc,
        int scoreId,
        StudentExamScoreInputDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult> DeleteAsync(string studentTc, int scoreId, CancellationToken cancellationToken);
}

public sealed class StudentExamScoreService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider) : IStudentExamScoreService
{
    public async Task<IReadOnlyList<StudentExamScoreDto>> GetForStudentAsync(
        string studentTc,
        CancellationToken cancellationToken) =>
        await dbContext.StudentExamScores.AsNoTracking()
            .Where(item => item.Tc == studentTc)
            .OrderBy(item => item.Exam.ExamName)
            .Select(item => new StudentExamScoreDto(
                item.ScoreId,
                item.ExamId,
                item.Exam.ExamName,
                item.Score,
                item.ExamDate))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StudentExamCatalogItemDto>> GetCatalogAsync(
        CancellationToken cancellationToken)
    {
        var exams = await dbContext.Exams.AsNoTracking()
            .OrderBy(item => item.ExamName)
            .ToListAsync(cancellationToken);
        return exams.Select(item => new StudentExamCatalogItemDto(
                item.ExamId,
                item.ExamName,
                ExamValidityPolicy.UsesHundredPointScale(item.ExamName) ? 100m : 999.99m))
            .ToArray();
    }

    public async Task<ServiceResult<StudentExamScoreDto>> CreateAsync(
        string studentTc,
        StudentExamScoreInputDto request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(studentTc, scoreId: null, request, cancellationToken);
        if (validation.Error is not null)
        {
            return ServiceResult<StudentExamScoreDto>.Failure(validation.Error, validation.StatusCode);
        }

        var score = new StudentExamScore
        {
            Tc = studentTc,
            ExamId = request.ExamId,
            Score = request.Score!.Value,
            ExamDate = request.ExamDate,
            Exam = validation.Exam!
        };
        dbContext.StudentExamScores.Add(score);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult<StudentExamScoreDto>.Success(Map(score), StatusCodes.Status201Created);
        }
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult<StudentExamScoreDto>.Failure(
                "Bu sınav için zaten bir sonuç kaydınız bulunuyor.",
                StatusCodes.Status409Conflict);
        }
    }

    public async Task<ServiceResult<StudentExamScoreDto>> UpdateAsync(
        string studentTc,
        int scoreId,
        StudentExamScoreInputDto request,
        CancellationToken cancellationToken)
    {
        var score = await dbContext.StudentExamScores
            .Include(item => item.Exam)
            .SingleOrDefaultAsync(
                item => item.ScoreId == scoreId && item.Tc == studentTc,
                cancellationToken);
        if (score is null)
        {
            return ServiceResult<StudentExamScoreDto>.Failure(
                "Sınav sonucu bulunamadı.",
                StatusCodes.Status404NotFound);
        }

        var validation = await ValidateAsync(studentTc, scoreId, request, cancellationToken);
        if (validation.Error is not null)
        {
            return ServiceResult<StudentExamScoreDto>.Failure(validation.Error, validation.StatusCode);
        }

        score.ExamId = request.ExamId;
        score.Exam = validation.Exam!;
        score.Score = request.Score!.Value;
        score.ExamDate = request.ExamDate;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult<StudentExamScoreDto>.Success(Map(score));
        }
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult<StudentExamScoreDto>.Failure(
                "Bu sınav için zaten bir sonuç kaydınız bulunuyor.",
                StatusCodes.Status409Conflict);
        }
    }

    public async Task<ServiceResult> DeleteAsync(
        string studentTc,
        int scoreId,
        CancellationToken cancellationToken)
    {
        var score = await dbContext.StudentExamScores.SingleOrDefaultAsync(
            item => item.ScoreId == scoreId && item.Tc == studentTc,
            cancellationToken);
        if (score is null)
        {
            return ServiceResult.Failure("Sınav sonucu bulunamadı.", StatusCodes.Status404NotFound);
        }

        dbContext.StudentExamScores.Remove(score);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult.Success();
    }

    private async Task<ScoreValidation> ValidateAsync(
        string studentTc,
        int? scoreId,
        StudentExamScoreInputDto request,
        CancellationToken cancellationToken)
    {
        if (!request.Score.HasValue || !request.ExamDate.HasValue)
        {
            return ScoreValidation.Failure("Puan ve sonuç tarihi zorunludur.", StatusCodes.Status400BadRequest);
        }

        if (request.ExamDate.Value > TodayInIstanbul())
        {
            return ScoreValidation.Failure("Sonuç tarihi gelecekte olamaz.", StatusCodes.Status400BadRequest);
        }

        var exam = await dbContext.Exams.SingleOrDefaultAsync(
            item => item.ExamId == request.ExamId,
            cancellationToken);
        if (exam is null)
        {
            return ScoreValidation.Failure("Seçilen sınav bulunamadı.", StatusCodes.Status404NotFound);
        }

        var maximumScore = ExamValidityPolicy.UsesHundredPointScale(exam.ExamName) ? 100m : 999.99m;
        if (request.Score.Value < 0 || request.Score.Value > maximumScore)
        {
            var error = maximumScore == 100m
                ? $"{exam.ExamName} puanı 0 ile 100 arasında olmalıdır."
                : $"{exam.ExamName} puanı 0 ile 999,99 arasında olmalıdır.";
            return ScoreValidation.Failure(error, StatusCodes.Status400BadRequest);
        }

        var duplicateExists = await dbContext.StudentExamScores.AnyAsync(
            item => item.Tc == studentTc
                && item.ExamId == request.ExamId
                && (!scoreId.HasValue || item.ScoreId != scoreId.Value),
            cancellationToken);
        return duplicateExists
            ? ScoreValidation.Failure(
                "Bu sınav için zaten bir sonuç kaydınız bulunuyor.",
                StatusCodes.Status409Conflict)
            : ScoreValidation.Success(exam);
    }

    private DateOnly TodayInIstanbul() =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime.AddHours(3));

    private static StudentExamScoreDto Map(StudentExamScore score) =>
        new(score.ScoreId, score.ExamId, score.Exam.ExamName, score.Score, score.ExamDate);

    private sealed record ScoreValidation(Exam? Exam, string? Error, int StatusCode)
    {
        public static ScoreValidation Success(Exam exam) => new(exam, null, StatusCodes.Status200OK);
        public static ScoreValidation Failure(string error, int statusCode) => new(null, error, statusCode);
    }
}
