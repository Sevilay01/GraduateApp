using System.Data;
using System.Text.Json;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GraduateApp.API.Services;

public interface IApplicationEvaluationService
{
    Task<AdminEvaluationPageDto?> GetAdminPageAsync(int offeringId, CancellationToken cancellationToken);
    Task<ServiceResult<EvaluationRankingPreviewDto>> PreviewAsync(int offeringId, CancellationToken cancellationToken);
    Task<ServiceResult> DecideEligibilityAsync(
        Guid applicationPublicId,
        int adminId,
        EligibilityDecisionDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult> SetManualScoreAsync(
        Guid applicationPublicId,
        Guid criterionPublicId,
        int adminId,
        ManualEvaluationScoreDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult> FinalizeAsync(
        int offeringId,
        int adminId,
        OfferingEvaluationCommandDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult<EvaluationPublicationSummaryDto>> GetPublicationSummaryAsync(
        int offeringId,
        CancellationToken cancellationToken);
    Task<ServiceResult> PublishAsync(
        int offeringId,
        int adminId,
        OfferingEvaluationCommandDto request,
        CancellationToken cancellationToken);
    Task<PublishedApplicationEvaluationDto?> GetPublishedForStudentAsync(
        string studentTc,
        Guid applicationPublicId,
        CancellationToken cancellationToken);
}

public sealed class ApplicationEvaluationService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider) : IApplicationEvaluationService
{
    public async Task<AdminEvaluationPageDto?> GetAdminPageAsync(
        int offeringId,
        CancellationToken cancellationToken)
    {
        var offering = await AdminPageQuery(asTracking: false)
            .Include(item => item.ExamRequirements).ThenInclude(item => item.Exam)
            .SingleOrDefaultAsync(item => item.ProgramOfferingId == offeringId, cancellationToken);
        return offering is null || !offering.UsesEvaluationWorkflow ? null : MapAdminPage(offering);
    }

    public async Task<ServiceResult<EvaluationRankingPreviewDto>> PreviewAsync(
        int offeringId,
        CancellationToken cancellationToken)
    {
        var offering = await AdminPageQuery(asTracking: false)
            .SingleOrDefaultAsync(item => item.ProgramOfferingId == offeringId, cancellationToken);
        if (offering is null || !offering.UsesEvaluationWorkflow)
        {
            return ServiceResult<EvaluationRankingPreviewDto>.Failure(
                "Değerlendirme ilanı bulunamadı.",
                StatusCodes.Status404NotFound);
        }

        return ServiceResult<EvaluationRankingPreviewDto>.Success(BuildPreview(offering, timeProvider.GetUtcNow().UtcDateTime));
    }

    public async Task<ServiceResult> DecideEligibilityAsync(
        Guid applicationPublicId,
        int adminId,
        EligibilityDecisionDto request,
        CancellationToken cancellationToken)
    {
        if (request.EligibilityStatus is not (EvaluationEligibilityStatus.Eligible or EvaluationEligibilityStatus.Ineligible))
        {
            return ServiceResult.Failure("Uygunluk kararı Eligible veya Ineligible olmalıdır.", StatusCodes.Status400BadRequest);
        }

        var normalizedReason = string.IsNullOrWhiteSpace(request.IneligibilityReason)
            ? null
            : request.IneligibilityReason.Trim();
        if (request.EligibilityStatus == EvaluationEligibilityStatus.Ineligible && normalizedReason is null)
        {
            return ServiceResult.Failure("Uygun olmayan aday için gerekçe zorunludur.", StatusCodes.Status400BadRequest);
        }

        if (request.EligibilityStatus == EvaluationEligibilityStatus.Eligible && normalizedReason is not null)
        {
            return ServiceResult.Failure("Uygun aday için uygun olmama gerekçesi gönderilemez.", StatusCodes.Status400BadRequest);
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var application = await dbContext.Applications
            .Include(item => item.ProgramOffering)
            .Include(item => item.Evaluation).ThenInclude(item => item!.Components)
            .Include(item => item.DocumentRequirementSnapshots).ThenInclude(item => item.Documents)
            .SingleOrDefaultAsync(item => item.PublicId == applicationPublicId, cancellationToken);
        if (application?.Evaluation is null || !application.UsesEvaluationWorkflow)
        {
            return ServiceResult.Failure("Başvuru değerlendirmesi bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (application.ProgramOffering.EvaluationState != OfferingEvaluationState.Configuring)
        {
            return ServiceResult.Failure("Kesinleştirilmiş veya yayımlanmış değerlendirme değiştirilemez.", StatusCodes.Status409Conflict);
        }

        if (application.CurrentStatus != ApplicationStatus.UnderReview.ToString())
        {
            return ServiceResult.Failure("Uygunluk kararı yalnızca incelemedeki başvuruya verilebilir.", StatusCodes.Status409Conflict);
        }

        if (request.EligibilityStatus == EvaluationEligibilityStatus.Eligible)
        {
            if (application.UsesDocumentWorkflow && application.DocumentRequirementSnapshots.Any(requirement =>
                requirement.IsRequired
                && !requirement.Documents.Any(document =>
                    document.IsCurrent && document.ReviewStatus == DocumentReviewStatus.Approved)))
            {
                return ServiceResult.Failure("Tüm zorunlu güncel belgeler onaylanmadan aday uygun işaretlenemez.", StatusCodes.Status409Conflict);
            }

            if (application.Evaluation.Components.Any(component =>
                component.SourceTypeSnapshot != EvaluationCriterionSourceType.ManualScore
                && (!component.RawScore.HasValue
                    || !component.NormalizedScore.HasValue
                    || !component.WeightedScore.HasValue)))
            {
                return ServiceResult.Failure("Otomatik değerlendirme bileşenlerinden biri tamamlanmamış.", StatusCodes.Status409Conflict);
            }
        }

        if (!TrySetRowVersion(application.Evaluation, request.RowVersion, out var rowVersionError))
        {
            return ServiceResult.Failure(rowVersionError!, StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        application.Evaluation.EligibilityStatus = request.EligibilityStatus;
        application.Evaluation.IneligibilityReason = normalizedReason;
        application.Evaluation.EligibilityDecidedByAdminId = adminId;
        application.Evaluation.EligibilityDecidedAtUtc = now;
        application.Evaluation.TotalScore = request.EligibilityStatus == EvaluationEligibilityStatus.Eligible
            ? CalculateTotalOrNull(application.Evaluation.Components)
            : null;
        TouchOffering(application.ProgramOffering, now);
        AddAudit(adminId, "ApplicationEligibilityDecided", "Application", application.PublicId.ToString(), new
        {
            application.ProgramOfferingId,
            Eligibility = request.EligibilityStatus
        }, now);

        return await SaveMutationAsync(transaction, cancellationToken);
    }

    public async Task<ServiceResult> SetManualScoreAsync(
        Guid applicationPublicId,
        Guid criterionPublicId,
        int adminId,
        ManualEvaluationScoreDto request,
        CancellationToken cancellationToken)
    {
        if (!request.RawScore.HasValue || request.RawScore.Value is < 0m or > 100m)
        {
            return ServiceResult.Failure("Manuel puan 0 ile 100 arasında olmalıdır.", StatusCodes.Status400BadRequest);
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var application = await dbContext.Applications
            .Include(item => item.ProgramOffering)
            .Include(item => item.Evaluation).ThenInclude(item => item!.Components)
            .SingleOrDefaultAsync(item => item.PublicId == applicationPublicId, cancellationToken);
        var component = application?.Evaluation?.Components.SingleOrDefault(item =>
            item.CriterionPublicIdSnapshot == criterionPublicId);
        if (application?.Evaluation is null || component is null || !application.UsesEvaluationWorkflow)
        {
            return ServiceResult.Failure("Başvuru değerlendirme bileşeni bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (application.ProgramOffering.EvaluationState != OfferingEvaluationState.Configuring
            || application.CurrentStatus != ApplicationStatus.UnderReview.ToString())
        {
            return ServiceResult.Failure("Manuel puan yalnızca incelemedeki ve kesinleşmemiş başvuruya girilebilir.", StatusCodes.Status409Conflict);
        }

        if (component.SourceTypeSnapshot != EvaluationCriterionSourceType.ManualScore)
        {
            return ServiceResult.Failure("Yalnızca manuel puan kriteri güncellenebilir.", StatusCodes.Status409Conflict);
        }

        if (!TrySetRowVersion(component, request.RowVersion, out var rowVersionError))
        {
            return ServiceResult.Failure(rowVersionError!, StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var score = EvaluationScoring.CalculateComponent(
            request.RawScore.Value,
            component.MaximumRawScoreSnapshot,
            component.WeightBasisPointsSnapshot);
        component.RawScore = request.RawScore.Value;
        component.NormalizedScore = score.NormalizedScore;
        component.WeightedScore = score.WeightedScore;
        component.ManualScoredByAdminId = adminId;
        component.ManualScoredAtUtc = now;
        application.Evaluation.TotalScore = application.Evaluation.EligibilityStatus == EvaluationEligibilityStatus.Eligible
            ? CalculateTotalOrNull(application.Evaluation.Components)
            : null;
        TouchOffering(application.ProgramOffering, now);
        AddAudit(adminId, "ApplicationManualScoreUpdated", "Application", application.PublicId.ToString(), new
        {
            application.ProgramOfferingId,
            CriterionPublicId = component.CriterionPublicIdSnapshot
        }, now);

        return await SaveMutationAsync(transaction, cancellationToken);
    }

    public async Task<ServiceResult> FinalizeAsync(
        int offeringId,
        int adminId,
        OfferingEvaluationCommandDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FinalizeCoreAsync(offeringId, adminId, request, cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            && IsSqlServerDeadlock(exception))
        {
            return ConcurrencyConflict();
        }
    }

    private async Task<ServiceResult> FinalizeCoreAsync(
        int offeringId,
        int adminId,
        OfferingEvaluationCommandDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var offering = await AdminPageQuery(asTracking: true)
            .SingleOrDefaultAsync(item => item.ProgramOfferingId == offeringId, cancellationToken);
        if (offering is null || !offering.UsesEvaluationWorkflow)
        {
            return ServiceResult.Failure("Değerlendirme ilanı bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (!TrySetRowVersion(offering, request.RowVersion, out var rowVersionError))
        {
            return ServiceResult.Failure(rowVersionError!, StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var preview = BuildPreview(offering, now);
        if (!preview.CanFinalize)
        {
            return ServiceResult.Failure(
                $"Sonuçlar kesinleştirilemedi: {string.Join(" ", preview.BlockingReasons)}",
                StatusCodes.Status409Conflict);
        }

        var ranked = RankEligible(offering).ToArray();
        for (var index = 0; index < ranked.Length; index++)
        {
            var evaluation = ranked[index].Evaluation!;
            evaluation.Rank = index + 1;
            evaluation.Outcome = index < offering.Quota
                ? EvaluationOutcome.Admitted
                : EvaluationOutcome.NotAdmitted;
            evaluation.FinalizedAtUtc = now;
        }

        var ineligible = offering.Applications
            .Where(item => item.CurrentStatus != ApplicationStatus.Draft.ToString()
                && item.CurrentStatus != ApplicationStatus.Withdrawn.ToString()
                && item.Evaluation?.EligibilityStatus == EvaluationEligibilityStatus.Ineligible)
            .Select(item => item.Evaluation!)
            .ToArray();
        foreach (var evaluation in ineligible)
        {
            evaluation.Rank = null;
            evaluation.TotalScore = null;
            evaluation.Outcome = EvaluationOutcome.Ineligible;
            evaluation.FinalizedAtUtc = now;
        }

        offering.IsOpen = false;
        offering.EvaluationState = OfferingEvaluationState.Finalized;
        offering.EvaluationFinalizedAtUtc = now;
        offering.UpdatedAtUtc = now;
        AddAudit(adminId, "OfferingEvaluationFinalized", "ProgramOffering", OfferingTarget(offering), new
        {
            offering.ProgramOfferingId,
            EligibleCount = ranked.Length,
            AdmittedCount = Math.Min(ranked.Length, offering.Quota),
            NotAdmittedCount = Math.Max(0, ranked.Length - offering.Quota),
            IneligibleCount = ineligible.Length,
            offering.Quota
        }, now);

        return await SaveMutationAsync(transaction, cancellationToken);
    }

    public async Task<ServiceResult<EvaluationPublicationSummaryDto>> GetPublicationSummaryAsync(
        int offeringId,
        CancellationToken cancellationToken)
    {
        var offering = await dbContext.ProgramOfferings.AsNoTracking()
            .Include(item => item.Applications).ThenInclude(item => item.Evaluation)
            .SingleOrDefaultAsync(item => item.ProgramOfferingId == offeringId, cancellationToken);
        if (offering is null || offering.EvaluationState != OfferingEvaluationState.Finalized)
        {
            return ServiceResult<EvaluationPublicationSummaryDto>.Failure(
                "Yayımlanabilir kesinleştirilmiş sonuç bulunamadı.",
                StatusCodes.Status409Conflict);
        }

        return ServiceResult<EvaluationPublicationSummaryDto>.Success(new EvaluationPublicationSummaryDto(
            offering.Quota,
            offering.Applications.Count(item => item.Evaluation?.Outcome == EvaluationOutcome.Admitted),
            offering.Applications.Count(item => item.Evaluation?.Outcome == EvaluationOutcome.NotAdmitted),
            offering.Applications.Count(item => item.Evaluation?.Outcome == EvaluationOutcome.Ineligible),
            Convert.ToBase64String(offering.RowVersion)));
    }

    public async Task<ServiceResult> PublishAsync(
        int offeringId,
        int adminId,
        OfferingEvaluationCommandDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var offering = await dbContext.ProgramOfferings
            .Include(item => item.Applications).ThenInclude(item => item.Evaluation)
            .SingleOrDefaultAsync(item => item.ProgramOfferingId == offeringId, cancellationToken);
        if (offering is null || !offering.UsesEvaluationWorkflow)
        {
            return ServiceResult.Failure("Değerlendirme ilanı bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (offering.EvaluationState != OfferingEvaluationState.Finalized)
        {
            return ServiceResult.Failure(
                offering.EvaluationState == OfferingEvaluationState.Published
                    ? "Sonuçlar daha önce yayımlanmış."
                    : "Sonuçlar yayımlanmadan önce kesinleştirilmelidir.",
                StatusCodes.Status409Conflict);
        }

        if (!TrySetRowVersion(offering, request.RowVersion, out var rowVersionError))
        {
            return ServiceResult.Failure(rowVersionError!, StatusCodes.Status400BadRequest);
        }

        var candidates = offering.Applications
            .Where(item => item.CurrentStatus != ApplicationStatus.Draft.ToString()
                && item.CurrentStatus != ApplicationStatus.Withdrawn.ToString())
            .ToArray();
        if (candidates.Length == 0 || candidates.Any(item => item.Evaluation?.Outcome is null))
        {
            return ServiceResult.Failure("Kesinleştirilmiş sonucu olmayan başvuru bulundu.", StatusCodes.Status409Conflict);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var application in candidates)
        {
            var next = application.Evaluation!.Outcome == EvaluationOutcome.Admitted
                ? ApplicationStatus.Approved
                : ApplicationStatus.Rejected;
            var previous = application.CurrentStatus;
            application.CurrentStatus = next.ToString();
            dbContext.ApplicationStatusHistories.Add(new ApplicationStatusHistory
            {
                ApplicationId = application.ApplicationId,
                PreviousStatus = previous,
                StatusName = next.ToString(),
                ChangedByAdminId = adminId,
                ChangeDate = now,
                Notes = "Kesinleştirilmiş değerlendirme sonucu yayımlandı."
            });
        }

        offering.EvaluationState = OfferingEvaluationState.Published;
        offering.ResultsPublishedAtUtc = now;
        offering.UpdatedAtUtc = now;
        AddAudit(adminId, "OfferingResultsPublished", "ProgramOffering", OfferingTarget(offering), new
        {
            offering.ProgramOfferingId,
            AdmittedCount = candidates.Count(item => item.Evaluation!.Outcome == EvaluationOutcome.Admitted),
            RejectedCount = candidates.Count(item => item.Evaluation!.Outcome != EvaluationOutcome.Admitted)
        }, now);

        return await SaveMutationAsync(transaction, cancellationToken);
    }

    public async Task<PublishedApplicationEvaluationDto?> GetPublishedForStudentAsync(
        string studentTc,
        Guid applicationPublicId,
        CancellationToken cancellationToken)
    {
        var application = await dbContext.Applications.AsNoTracking()
            .Where(item => item.Tc == studentTc
                && item.PublicId == applicationPublicId
                && item.UsesEvaluationWorkflow
                && item.ProgramOffering.EvaluationState == OfferingEvaluationState.Published)
            .Include(item => item.ProgramOffering)
            .Include(item => item.Evaluation).ThenInclude(item => item!.Components)
            .SingleOrDefaultAsync(cancellationToken);
        if (application?.Evaluation?.Outcome is null || !application.ProgramOffering.ResultsPublishedAtUtc.HasValue)
        {
            return null;
        }

        return new PublishedApplicationEvaluationDto(
            application.Evaluation.Outcome.Value,
            application.Evaluation.TotalScore,
            application.Evaluation.Rank,
            DateTime.SpecifyKind(application.ProgramOffering.ResultsPublishedAtUtc.Value, DateTimeKind.Utc),
            application.Evaluation.Components
                .Where(item => item.RawScore.HasValue && item.NormalizedScore.HasValue && item.WeightedScore.HasValue)
                .OrderBy(item => item.TieBreakPrioritySnapshot)
                .Select(item => new PublishedEvaluationComponentDto(
                    item.DisplayNameSnapshot,
                    item.RawScore!.Value,
                    item.NormalizedScore!.Value,
                    item.WeightBasisPointsSnapshot,
                    item.WeightedScore!.Value))
                .ToArray());
    }

    private IQueryable<ProgramOffering> AdminPageQuery(bool asTracking)
    {
        var query = dbContext.ProgramOfferings
            .Include(item => item.Program)
            .Include(item => item.EvaluationCriteria).ThenInclude(item => item.Exam)
            .Include(item => item.Applications).ThenInclude(item => item.TcNavigation)
            .Include(item => item.Applications).ThenInclude(item => item.Evaluation).ThenInclude(item => item!.Components)
            .Include(item => item.Applications).ThenInclude(item => item.DocumentRequirementSnapshots).ThenInclude(item => item.Documents)
            .AsSplitQuery();
        return asTracking ? query : query.AsNoTracking();
    }

    private EvaluationRankingPreviewDto BuildPreview(ProgramOffering offering, DateTime now)
    {
        var blockers = new List<string>();
        if (offering.EvaluationState != OfferingEvaluationState.Configuring)
        {
            blockers.Add("İlan sonuçları zaten kesinleştirilmiş veya yayımlanmış.");
        }

        if (offering.IsOpen)
        {
            blockers.Add("İlan önce kapatılmalıdır.");
        }

        if (!offering.ApplicationDeadlineUtc.HasValue || now <= offering.ApplicationDeadlineUtc.Value)
        {
            blockers.Add("Son başvuru tarihi henüz geçmedi.");
        }

        if (offering.Quota <= 0)
        {
            blockers.Add("Kontenjan pozitif olmalıdır.");
        }

        if (!PolicyIsValid(offering.EvaluationCriteria))
        {
            blockers.Add("Değerlendirme politikası geçerli ve 10000 basis point olmalıdır.");
        }

        var submitted = offering.Applications.Where(item => item.CurrentStatus != ApplicationStatus.Draft.ToString()
            && item.CurrentStatus != ApplicationStatus.Withdrawn.ToString()).ToArray();
        if (submitted.Length == 0)
        {
            blockers.Add("Kesinleştirilecek gönderilmiş başvuru bulunmuyor.");
        }
        if (submitted.Any(item => item.CurrentStatus == ApplicationStatus.Pending.ToString()))
        {
            blockers.Add("İncelemeye alınmamış bekleyen başvurular var.");
        }

        if (submitted.Any(item => item.CurrentStatus != ApplicationStatus.UnderReview.ToString()))
        {
            blockers.Add("Tüm değerlendirilecek başvurular incelemede olmalıdır.");
        }

        if (submitted.Any(item => item.Evaluation is null
            || item.Evaluation.EligibilityStatus == EvaluationEligibilityStatus.Pending))
        {
            blockers.Add("Uygunluk kararı verilmemiş başvurular var.");
        }

        foreach (var application in submitted.Where(item =>
            item.Evaluation?.EligibilityStatus == EvaluationEligibilityStatus.Eligible))
        {
            var incomplete = application.Evaluation!.Components
                .Where(item => !item.RawScore.HasValue || !item.NormalizedScore.HasValue || !item.WeightedScore.HasValue)
                .Select(item => item.DisplayNameSnapshot)
                .ToArray();
            if (incomplete.Length > 0)
            {
                blockers.Add($"{application.PublicId:D} başvurusunda eksik kriterler: {string.Join(", ", incomplete)}.");
            }
            else
            {
                application.Evaluation.TotalScore = EvaluationScoring.Total(
                    application.Evaluation.Components.Select(item => item.WeightedScore!.Value));
            }
        }

        var rows = RankEligible(offering)
            .Select((application, index) => new EvaluationRankingRowDto(
                application.PublicId,
                $"{application.TcNavigation.StudentName} {application.TcNavigation.StudentSurname}".Trim(),
                MaskTc(application.Tc),
                application.Evaluation!.TotalScore!.Value,
                index + 1,
                index < offering.Quota ? EvaluationOutcome.Admitted : EvaluationOutcome.NotAdmitted,
                application.Evaluation.Components.OrderBy(item => item.TieBreakPrioritySnapshot).Select(MapComponent).ToArray()))
            .ToArray();
        return new EvaluationRankingPreviewDto(blockers.Count == 0, blockers.Distinct().ToArray(), rows);
    }

    private static IEnumerable<Application> RankEligible(ProgramOffering offering)
    {
        var eligible = offering.Applications.Where(item =>
            item.CurrentStatus == ApplicationStatus.UnderReview.ToString()
            && item.Evaluation?.EligibilityStatus == EvaluationEligibilityStatus.Eligible
            && item.Evaluation.TotalScore.HasValue
            && item.Evaluation.Components.All(component => component.NormalizedScore.HasValue));
        IOrderedEnumerable<Application> ordered = eligible.OrderByDescending(item => item.Evaluation!.TotalScore!.Value);
        foreach (var priority in offering.EvaluationCriteria.OrderBy(item => item.TieBreakPriority).Select(item => item.TieBreakPriority))
        {
            ordered = ordered.ThenByDescending(item => item.Evaluation!.Components
                .Single(component => component.TieBreakPrioritySnapshot == priority).NormalizedScore!.Value);
        }

        return ordered
            .ThenBy(item => item.ApplicationDate)
            .ThenBy(item => item.PublicId);
    }

    private static bool PolicyIsValid(IEnumerable<ProgramOfferingEvaluationCriterion> criteria)
    {
        var items = criteria.ToArray();
        return items.Length > 0
            && items.Sum(item => item.WeightBasisPoints) == EvaluationScoring.TotalWeightBasisPoints
            && items.All(item => item.WeightBasisPoints > 0
                && item.TieBreakPriority > 0
                && item.MaximumRawScore > 0m)
            && items.Select(item => item.NormalizedCode).Distinct(StringComparer.Ordinal).Count() == items.Length
            && items.Select(item => item.TieBreakPriority).Distinct().Count() == items.Length;
    }

    private static decimal? CalculateTotalOrNull(IEnumerable<ApplicationEvaluationComponent> components)
    {
        var items = components.ToArray();
        return items.Any(item => !item.WeightedScore.HasValue)
            ? null
            : EvaluationScoring.Total(items.Select(item => item.WeightedScore!.Value));
    }

    private async Task<ServiceResult> SaveMutationAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
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
            return ConcurrencyConflict();
        }
        catch (Exception exception) when (IsSqlServerDeadlock(exception))
        {
            return ConcurrencyConflict();
        }
        catch (DbUpdateException)
        {
            return ServiceResult.Failure(
                "Değerlendirme işlemi atomik olarak tamamlanamadı. Verileri yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
    }

    private bool TrySetRowVersion<TEntity>(TEntity entity, string value, out string? error)
        where TEntity : class
    {
        try
        {
            var rowVersion = Convert.FromBase64String(value);
            dbContext.Entry(entity).Property("RowVersion").OriginalValue = rowVersion;
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
        string targetType,
        string targetId,
        object details,
        DateTime now) =>
        dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorAdminId = adminId,
            EventType = eventType,
            TargetType = targetType,
            TargetId = targetId,
            Details = JsonSerializer.Serialize(details),
            CreatedAtUtc = now
        });

    private static AdminEvaluationPageDto MapAdminPage(ProgramOffering offering) => new(
        offering.ProgramOfferingId,
        offering.Program.ProgramName,
        offering.AcademicYearStart,
        AcademicPeriodFormatter.FormatAcademicYear(offering.AcademicYearStart),
        offering.Term,
        AcademicPeriodFormatter.FormatTerm(offering.Term),
        offering.ApplicationStartUtc.HasValue ? DateTime.SpecifyKind(offering.ApplicationStartUtc.Value, DateTimeKind.Utc) : null,
        offering.ApplicationDeadlineUtc.HasValue ? DateTime.SpecifyKind(offering.ApplicationDeadlineUtc.Value, DateTimeKind.Utc) : null,
        offering.Quota,
        offering.IsOpen,
        offering.UsesEvaluationWorkflow,
        offering.EvaluationState,
        offering.EvaluationFinalizedAtUtc.HasValue ? DateTime.SpecifyKind(offering.EvaluationFinalizedAtUtc.Value, DateTimeKind.Utc) : null,
        offering.ResultsPublishedAtUtc.HasValue ? DateTime.SpecifyKind(offering.ResultsPublishedAtUtc.Value, DateTimeKind.Utc) : null,
        Convert.ToBase64String(offering.RowVersion),
        offering.EvaluationCriteria.OrderBy(item => item.TieBreakPriority).Select(MapCriterion).ToArray(),
        offering.Applications
            .Where(item => item.CurrentStatus != ApplicationStatus.Draft.ToString())
            .OrderBy(item => item.ApplicationDate)
            .ThenBy(item => item.PublicId)
            .Select(item => new AdminEvaluationApplicationDto(
                item.PublicId,
                $"{item.TcNavigation.StudentName} {item.TcNavigation.StudentSurname}".Trim(),
                MaskTc(item.Tc),
                ParseStatus(item.CurrentStatus),
                DocumentSummary(item),
                item.Evaluation?.EligibilityStatus ?? EvaluationEligibilityStatus.Pending,
                item.Evaluation?.IneligibilityReason,
                item.Evaluation?.TotalScore,
                item.Evaluation?.Rank,
                item.Evaluation?.Outcome,
                item.Evaluation is null ? string.Empty : Convert.ToBase64String(item.Evaluation.RowVersion),
                item.Evaluation?.Components.OrderBy(component => component.TieBreakPrioritySnapshot).Select(MapComponent).ToArray() ?? []))
            .ToArray(),
        offering.ExamRequirements
            .Where(item => item.IsRequired)
            .OrderBy(item => item.Exam.ExamName)
            .ThenBy(item => item.ExamId)
            .Select(item => new ExamRequirementDto(
                item.ExamId,
                item.Exam.ExamName,
                item.MinimumScore,
                item.MinimumValidityDate,
                item.IsRequired))
            .ToArray());

    private static ProgramOfferingEvaluationCriterionDto MapCriterion(ProgramOfferingEvaluationCriterion item) => new(
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

    private static EvaluationComponentDto MapComponent(ApplicationEvaluationComponent item) => new(
        item.CriterionPublicIdSnapshot,
        item.CodeSnapshot,
        item.DisplayNameSnapshot,
        item.SourceTypeSnapshot,
        item.RawScore,
        item.MaximumRawScoreSnapshot,
        item.NormalizedScore,
        item.WeightBasisPointsSnapshot,
        item.WeightedScore,
        item.TieBreakPrioritySnapshot,
        Convert.ToBase64String(item.RowVersion));

    private static string DocumentSummary(Application application)
    {
        if (!application.UsesDocumentWorkflow)
        {
            return "Legacy belge akışı";
        }

        var required = application.DocumentRequirementSnapshots.Where(item => item.IsRequired).ToArray();
        var approved = required.Count(item => item.Documents.Any(document =>
            document.IsCurrent && document.ReviewStatus == DocumentReviewStatus.Approved));
        return $"{approved}/{required.Length} zorunlu belge onaylı";
    }

    private static ApplicationStatus ParseStatus(string value) =>
        ApplicationStatusRules.TryParseStoredValue(value, out var status) ? status : ApplicationStatus.Pending;

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private static ServiceResult ConcurrencyConflict() =>
        ServiceResult.Failure(
            "Değerlendirme veya ilan başka bir yönetici tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
            StatusCodes.Status409Conflict);

    private static bool IsSqlServerDeadlock(Exception exception) =>
        exception is SqlException { Number: 1205 }
        || (exception.InnerException is not null && IsSqlServerDeadlock(exception.InnerException));

    private static string MaskTc(string tc) => tc.Length >= 4 ? $"*******{tc[^4..]}" : "***********";
    private static string OfferingTarget(ProgramOffering offering) => offering.ProgramOfferingId.ToString();
}
