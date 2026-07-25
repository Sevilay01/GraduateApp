using System.Data;
using System.Text.Json;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GraduateApp.API.Services;

public interface IEvaluationPolicyService
{
    Task<IReadOnlyList<ProgramOfferingEvaluationCriterionDto>> GetAsync(int offeringId, CancellationToken cancellationToken);
    Task<ServiceResult<ProgramOfferingEvaluationCriterionDto>> CreateAsync(
        int offeringId,
        int adminId,
        EvaluationCriterionCreateDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult<ProgramOfferingEvaluationCriterionDto>> UpdateAsync(
        int offeringId,
        Guid publicId,
        int adminId,
        EvaluationCriterionUpdateDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult> DeleteAsync(
        int offeringId,
        Guid publicId,
        int adminId,
        EvaluationCriterionDeleteDto request,
        CancellationToken cancellationToken);
}

public sealed class EvaluationPolicyService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider) : IEvaluationPolicyService
{
    public async Task<IReadOnlyList<ProgramOfferingEvaluationCriterionDto>> GetAsync(
        int offeringId,
        CancellationToken cancellationToken) =>
        (await dbContext.ProgramOfferingEvaluationCriteria.AsNoTracking()
            .Where(item => item.ProgramOfferingId == offeringId)
            .Include(item => item.Exam)
            .OrderBy(item => item.TieBreakPriority)
            .ThenBy(item => item.CriterionId)
            .ToListAsync(cancellationToken))
        .Select(Map)
        .ToArray();

    public async Task<ServiceResult<ProgramOfferingEvaluationCriterionDto>> CreateAsync(
        int offeringId,
        int adminId,
        EvaluationCriterionCreateDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var offering = await LoadAggregateAsync(offeringId, cancellationToken);
        if (offering is null)
        {
            return NotFound();
        }

        var guard = GuardMutable(offering);
        if (guard is not null)
        {
            return ServiceResult<ProgramOfferingEvaluationCriterionDto>.Failure(guard, StatusCodes.Status409Conflict);
        }

        var validation = Validate(offering, null, request);
        if (validation is not null)
        {
            return ServiceResult<ProgramOfferingEvaluationCriterionDto>.Failure(validation.Value.Message, validation.Value.StatusCode);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var criterion = new ProgramOfferingEvaluationCriterion
        {
            PublicId = Guid.NewGuid(),
            ProgramOfferingId = offeringId,
            Code = request.Code.Trim(),
            NormalizedCode = NormalizeCode(request.Code),
            DisplayName = request.DisplayName.Trim(),
            SourceType = request.SourceType,
            ExamId = request.ExamId,
            WeightBasisPoints = request.WeightBasisPoints,
            MaximumRawScore = request.MaximumRawScore,
            TieBreakPriority = request.TieBreakPriority,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        offering.EvaluationCriteria.Add(criterion);
        TouchOffering(offering, now);
        AddAudit(adminId, "EvaluationCriterionCreated", criterion, now);

        return await SaveCriterionAsync(criterion, transaction, StatusCodes.Status201Created, cancellationToken);
    }

    public async Task<ServiceResult<ProgramOfferingEvaluationCriterionDto>> UpdateAsync(
        int offeringId,
        Guid publicId,
        int adminId,
        EvaluationCriterionUpdateDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var offering = await LoadAggregateAsync(offeringId, cancellationToken);
        var criterion = offering?.EvaluationCriteria.SingleOrDefault(item => item.PublicId == publicId);
        if (offering is null || criterion is null)
        {
            return NotFound();
        }

        var guard = GuardMutable(offering);
        if (guard is not null)
        {
            return ServiceResult<ProgramOfferingEvaluationCriterionDto>.Failure(guard, StatusCodes.Status409Conflict);
        }

        var validation = Validate(offering, criterion.CriterionId, request);
        if (validation is not null)
        {
            return ServiceResult<ProgramOfferingEvaluationCriterionDto>.Failure(validation.Value.Message, validation.Value.StatusCode);
        }

        if (!TrySetRowVersion(criterion, request.RowVersion, out var rowVersionError))
        {
            return ServiceResult<ProgramOfferingEvaluationCriterionDto>.Failure(rowVersionError!, StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        criterion.Code = request.Code.Trim();
        criterion.NormalizedCode = NormalizeCode(request.Code);
        criterion.DisplayName = request.DisplayName.Trim();
        criterion.SourceType = request.SourceType;
        criterion.ExamId = request.ExamId;
        criterion.WeightBasisPoints = request.WeightBasisPoints;
        criterion.MaximumRawScore = request.MaximumRawScore;
        criterion.TieBreakPriority = request.TieBreakPriority;
        criterion.UpdatedAtUtc = now;
        TouchOffering(offering, now);
        AddAudit(adminId, "EvaluationCriterionUpdated", criterion, now);

        return await SaveCriterionAsync(criterion, transaction, StatusCodes.Status200OK, cancellationToken);
    }

    public async Task<ServiceResult> DeleteAsync(
        int offeringId,
        Guid publicId,
        int adminId,
        EvaluationCriterionDeleteDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var offering = await LoadAggregateAsync(offeringId, cancellationToken);
        var criterion = offering?.EvaluationCriteria.SingleOrDefault(item => item.PublicId == publicId);
        if (offering is null || criterion is null)
        {
            return ServiceResult.Failure("Değerlendirme kriteri bulunamadı.", StatusCodes.Status404NotFound);
        }

        var guard = GuardMutable(offering);
        if (guard is not null)
        {
            return ServiceResult.Failure(guard, StatusCodes.Status409Conflict);
        }

        if (!TrySetRowVersion(criterion, request.RowVersion, out var rowVersionError))
        {
            return ServiceResult.Failure(rowVersionError!, StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        dbContext.ProgramOfferingEvaluationCriteria.Remove(criterion);
        TouchOffering(offering, now);
        AddAudit(adminId, "EvaluationCriterionDeleted", criterion, now);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyFailure();
        }
        catch (Exception exception) when (
            DatabaseExceptionClassifier.IsDeadlock(exception)
            && !DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ConcurrencyFailure();
        }
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult.Failure("Değerlendirme kriteri kaldırılamadı.", StatusCodes.Status409Conflict);
        }
    }

    private (string Message, int StatusCode)? Validate(
        ProgramOffering offering,
        int? currentCriterionId,
        EvaluationCriterionCreateDto request)
    {
        var normalizedCode = NormalizeCode(request.Code);
        if (normalizedCode.Length is < 2 or > 64
            || normalizedCode.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            return ("Kriter kodu geçersiz.", StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName)
            || request.DisplayName.Trim().Length is < 2 or > 150
            || request.WeightBasisPoints is <= 0 or > EvaluationScoring.TotalWeightBasisPoints
            || request.TieBreakPriority <= 0)
        {
            return ("Kriter adı, ağırlığı veya eşitlik bozma önceliği geçersiz.", StatusCodes.Status400BadRequest);
        }

        var others = offering.EvaluationCriteria.Where(item => item.CriterionId != currentCriterionId).ToArray();
        if (others.Any(item => item.NormalizedCode == normalizedCode))
        {
            return ("Aynı kriter kodu bu ilanda zaten kullanılıyor.", StatusCodes.Status409Conflict);
        }

        if (others.Any(item => item.TieBreakPriority == request.TieBreakPriority))
        {
            return ("Eşitlik bozma önceliği ilan içinde benzersiz olmalıdır.", StatusCodes.Status409Conflict);
        }

        if (others.Sum(item => item.WeightBasisPoints) + request.WeightBasisPoints > EvaluationScoring.TotalWeightBasisPoints)
        {
            return ("Değerlendirme ağırlıkları toplamı 10000 basis point değerini aşamaz.", StatusCodes.Status409Conflict);
        }

        return request.SourceType switch
        {
            EvaluationCriterionSourceType.UndergraduateGpa when request.ExamId is not null || request.MaximumRawScore != 4m =>
                ("Lisans GNO kriterinde sınav seçilmemeli ve maksimum ham puan 4 olmalıdır.", StatusCodes.Status400BadRequest),
            EvaluationCriterionSourceType.UndergraduateGpa when others.Any(item => item.SourceType == EvaluationCriterionSourceType.UndergraduateGpa) =>
                ("Bir ilanda yalnızca bir lisans GNO kriteri tanımlanabilir.", StatusCodes.Status409Conflict),
            EvaluationCriterionSourceType.ExamScore when request.ExamId is null || request.MaximumRawScore <= 0m =>
                ("Sınav puanı kriterinde sınav ve pozitif maksimum ham puan zorunludur.", StatusCodes.Status400BadRequest),
            EvaluationCriterionSourceType.ExamScore when !offering.ExamRequirements.Any(item =>
                item.ExamId == request.ExamId && item.IsRequired) =>
                ("Sınav puanı kriteri ilanın zorunlu sınav koşullarından birini kullanmalıdır.", StatusCodes.Status409Conflict),
            EvaluationCriterionSourceType.ExamScore when others.Any(item => item.ExamId == request.ExamId) =>
                ("Aynı sınav değerlendirme politikasında birden fazla kez kullanılamaz.", StatusCodes.Status409Conflict),
            EvaluationCriterionSourceType.ManualScore when request.ExamId is not null || request.MaximumRawScore != 100m =>
                ("Manuel puan kriterinde sınav seçilmemeli ve maksimum ham puan 100 olmalıdır.", StatusCodes.Status400BadRequest),
            _ => null
        };
    }

    private static string? GuardMutable(ProgramOffering offering)
    {
        if (!offering.UsesEvaluationWorkflow)
        {
            return "Bu ilan değerlendirme iş akışını kullanmıyor.";
        }

        if (offering.EvaluationState != OfferingEvaluationState.Configuring)
        {
            return "Kesinleştirilmiş veya yayımlanmış değerlendirme politikası değiştirilemez.";
        }

        if (offering.IsOpen)
        {
            return "Açık bir ilanın değerlendirme politikası değiştirilemez. Önce ilanı kapatın.";
        }

        return offering.Applications.Count > 0
            ? "Taslak dâhil başvurusu bulunan bir ilanın değerlendirme politikası değiştirilemez."
            : null;
    }

    private async Task<ServiceResult<ProgramOfferingEvaluationCriterionDto>> SaveCriterionAsync(
        ProgramOfferingEvaluationCriterion criterion,
        IDbContextTransaction? transaction,
        int successStatusCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            if (criterion.ExamId.HasValue)
            {
                await dbContext.Entry(criterion).Reference(item => item.Exam).LoadAsync(cancellationToken);
            }

            return ServiceResult<ProgramOfferingEvaluationCriterionDto>.Success(Map(criterion), successStatusCode);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<ProgramOfferingEvaluationCriterionDto>.Failure(
                "Değerlendirme kriteri veya ilan başka bir yönetici tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
        catch (Exception exception) when (
            DatabaseExceptionClassifier.IsDeadlock(exception)
            && !DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult<ProgramOfferingEvaluationCriterionDto>.Failure(
                "Değerlendirme kriteri veya ilan başka bir yönetici tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult<ProgramOfferingEvaluationCriterionDto>.Failure(
                "Değerlendirme kriteri kaydedilemedi; kod, sınav ve öncelik alanlarını kontrol edin.",
                StatusCodes.Status409Conflict);
        }
    }

    private Task<ProgramOffering?> LoadAggregateAsync(int offeringId, CancellationToken cancellationToken) =>
        PolicyAggregateQuery()
            .SingleOrDefaultAsync(item => item.ProgramOfferingId == offeringId, cancellationToken);

    private IQueryable<ProgramOffering> PolicyAggregateQuery() =>
        dbContext.ProgramOfferings
            .Include(item => item.Applications)
            .Include(item => item.ExamRequirements)
            .Include(item => item.EvaluationCriteria).ThenInclude(item => item.Exam)
            .AsSplitQuery();

    private bool TrySetRowVersion(
        ProgramOfferingEvaluationCriterion criterion,
        string value,
        out string? error)
    {
        try
        {
            dbContext.Entry(criterion).Property(item => item.RowVersion).OriginalValue = Convert.FromBase64String(value);
            error = null;
            return true;
        }
        catch (FormatException)
        {
            error = "Eşzamanlılık belirteci geçersiz.";
            return false;
        }
    }

    private void TouchOffering(ProgramOffering offering, DateTime now)
    {
        offering.UpdatedAtUtc = now;
        dbContext.Entry(offering).Property(item => item.UpdatedAtUtc).IsModified = true;
    }

    private void AddAudit(
        int adminId,
        string eventType,
        ProgramOfferingEvaluationCriterion criterion,
        DateTime now) =>
        dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorAdminId = adminId,
            EventType = eventType,
            TargetType = "ProgramOfferingEvaluationCriterion",
            TargetId = criterion.PublicId.ToString("D"),
            Details = JsonSerializer.Serialize(new
            {
                criterion.ProgramOfferingId,
                criterion.Code,
                criterion.SourceType,
                criterion.WeightBasisPoints,
                criterion.TieBreakPriority
            }),
            CreatedAtUtc = now
        });

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private static ProgramOfferingEvaluationCriterionDto Map(ProgramOfferingEvaluationCriterion item) => new(
        item.PublicId,
        item.Code,
        item.DisplayName,
        item.SourceType,
        item.ExamId,
        item.Exam?.ExamName,
        item.WeightBasisPoints,
        item.MaximumRawScore,
        item.TieBreakPriority,
        Convert.ToBase64String(item.RowVersion));

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();

    private static ServiceResult<ProgramOfferingEvaluationCriterionDto> NotFound() =>
        ServiceResult<ProgramOfferingEvaluationCriterionDto>.Failure(
            "Değerlendirme ilanı veya kriteri bulunamadı.",
            StatusCodes.Status404NotFound);

    private static ServiceResult ConcurrencyFailure() =>
        ServiceResult.Failure(
            "Değerlendirme kriteri veya ilan başka bir yönetici tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
            StatusCodes.Status409Conflict);

}
