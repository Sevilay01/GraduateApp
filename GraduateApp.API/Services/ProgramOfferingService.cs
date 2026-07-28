using System.Data;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GraduateApp.API.Services;

public interface IProgramOfferingService
{
    Task<IReadOnlyList<ProgramOfferingAdminDto>> GetForAdminAsync(
        int? academicYearStart,
        AcademicTerm? term,
        bool includeArchived,
        CancellationToken cancellationToken);
    Task<ProgramOfferingCatalogDto> GetCatalogAsync(CancellationToken cancellationToken);
    Task<ServiceResult<ProgramOfferingAdminDto>> CreateAsync(
        int adminId,
        ProgramOfferingCreateDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult<ProgramOfferingAdminDto>> UpdateAsync(
        int offeringId,
        int adminId,
        ProgramOfferingUpdateDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult<ProgramOfferingAdminDto>> CloseInvalidForRemediationAsync(
        int offeringId,
        int adminId,
        ProgramOfferingRemediationCloseDto request,
        CancellationToken cancellationToken);
}

public sealed class ProgramOfferingService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider) : IProgramOfferingService
{
    private static readonly string DraftStatus = ApplicationStatus.Draft.ToString();

    public async Task<IReadOnlyList<ProgramOfferingAdminDto>> GetForAdminAsync(
        int? academicYearStart,
        AcademicTerm? term,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var offerings = await AdminListQuery(academicYearStart, term, includeArchived)
            .ToListAsync(cancellationToken);
        return offerings
            .Select(item => Map(
                item.Offering,
                item.DocumentRequirementCount,
                item.ActiveRequiredDocumentRequirementCount,
                item.HasActiveRequiredDocumentRequirement,
                item.DraftApplicationCount,
                item.SubmittedOrLaterApplicationCount))
            .ToArray();
    }

    private IQueryable<ProgramOfferingAdminListRow> AdminListQuery(
        int? academicYearStart,
        AcademicTerm? term,
        bool includeArchived)
    {
        var query = dbContext.ProgramOfferings.AsNoTracking()
            .Include(item => item.Program).ThenInclude(item => item.Institute)
            .Include(item => item.ExamRequirements).ThenInclude(item => item.Exam)
            .AsQueryable();
        if (academicYearStart.HasValue)
        {
            query = query.Where(item => item.AcademicYearStart == academicYearStart.Value);
        }

        if (term.HasValue)
        {
            query = query.Where(item => item.Term == term.Value);
        }

        if (!includeArchived)
        {
            query = query.Where(item => !item.IsArchived);
        }

        return query
            .OrderByDescending(item => item.AcademicYearStart)
            .ThenBy(item => item.Term)
            .ThenBy(item => item.Program.Institute.InstituteName)
            .ThenBy(item => item.Program.ProgramName)
            .ThenBy(item => item.Program.DegreeType)
            .ThenBy(item => item.ProgramOfferingId)
            .Select(item => new ProgramOfferingAdminListRow(
                item,
                item.DocumentRequirements.Count,
                item.DocumentRequirements.Count(requirement => requirement.IsActive && requirement.IsRequired),
                item.DocumentRequirements.Any(requirement => requirement.IsActive && requirement.IsRequired),
                item.Applications.Count(application => application.CurrentStatus == DraftStatus),
                item.Applications.Count(application => application.CurrentStatus != DraftStatus)));
    }

    public async Task<ProgramOfferingCatalogDto> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var programs = await dbContext.Programs.AsNoTracking()
            .Where(item => item.IsActive && item.Institute.IsActive)
            .OrderBy(item => item.Institute.InstituteName)
            .ThenBy(item => item.ProgramName)
            .ThenBy(item => item.DegreeType)
            .ThenBy(item => item.ProgramId)
            .Select(item => new ProgramCatalogItemDto(
                item.ProgramId,
                item.ProgramName,
                item.Institute.InstituteName,
                item.DegreeType))
            .ToListAsync(cancellationToken);
        var exams = await dbContext.Exams.AsNoTracking()
            .OrderBy(item => item.ExamName)
            .ThenBy(item => item.ExamId)
            .Select(item => new ExamCatalogItemDto(item.ExamId, item.ExamName))
            .ToListAsync(cancellationToken);
        return new ProgramOfferingCatalogDto(programs, exams);
    }

    public async Task<ServiceResult<ProgramOfferingAdminDto>> CreateAsync(
        int adminId,
        ProgramOfferingCreateDto request,
        CancellationToken cancellationToken)
    {
        if (request.IsOpen)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "Yeni ilan önce kapalı oluşturulmalıdır. En az bir zorunlu belge koşulu tanımlandıktan sonra ilanı açabilirsiniz.",
                StatusCodes.Status409Conflict);
        }

        var validationError = await ValidateRequestAsync(request, null, cancellationToken);
        if (validationError is not null)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(validationError.Message, validationError.StatusCode);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (request.IsOpen && request.ApplicationDeadlineUtc <= now)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "Süresi geçmiş bir ilan açılamaz.",
                StatusCodes.Status409Conflict);
        }

        var offering = new ProgramOffering
        {
            ProgramId = request.ProgramId,
            AcademicYearStart = request.AcademicYearStart,
            Term = request.Term,
            ApplicationStartUtc = EnsureUtc(request.ApplicationStartUtc),
            ApplicationDeadlineUtc = EnsureUtc(request.ApplicationDeadlineUtc),
            Quota = request.Quota,
            IsOpen = false,
            IsArchived = false,
            UsesEvaluationWorkflow = true,
            EvaluationState = OfferingEvaluationState.Configuring,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ExamRequirements = request.ExamRequirements.Select(MapRequirement).ToList()
        };
        dbContext.ProgramOfferings.Add(offering);
        AddAudit(adminId, "ProgramOfferingCreated", offering, now);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "Aynı program, akademik yıl ve dönem için zaten ilan bulunuyor.",
                StatusCodes.Status409Conflict);
        }

        await dbContext.Entry(offering).Reference(item => item.Program).LoadAsync(cancellationToken);
        await dbContext.Entry(offering.Program).Reference(item => item.Institute).LoadAsync(cancellationToken);
        foreach (var requirement in offering.ExamRequirements)
        {
            await dbContext.Entry(requirement).Reference(item => item.Exam).LoadAsync(cancellationToken);
        }

        return ServiceResult<ProgramOfferingAdminDto>.Success(Map(offering), StatusCodes.Status201Created);
    }

    public async Task<ServiceResult<ProgramOfferingAdminDto>> UpdateAsync(
        int offeringId,
        int adminId,
        ProgramOfferingUpdateDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await UpdateCoreAsync(offeringId, adminId, request, cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            && DatabaseExceptionClassifier.IsDeadlock(exception)
            && !DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "İlan başka bir kullanıcı tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
    }

    public async Task<ServiceResult<ProgramOfferingAdminDto>> CloseInvalidForRemediationAsync(
        int offeringId,
        int adminId,
        ProgramOfferingRemediationCloseDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CloseInvalidForRemediationCoreAsync(
                offeringId,
                adminId,
                request,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            && DatabaseExceptionClassifier.IsDeadlock(exception)
            && !DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "İlan başka bir kullanıcı tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
    }

    private async Task<ServiceResult<ProgramOfferingAdminDto>> CloseInvalidForRemediationCoreAsync(
        int offeringId,
        int adminId,
        ProgramOfferingRemediationCloseDto request,
        CancellationToken cancellationToken)
    {
        byte[] rowVersion;
        try
        {
            rowVersion = Convert.FromBase64String(request.RowVersion);
        }
        catch (FormatException)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "Eş zamanlılık belirteci geçersiz.",
                StatusCodes.Status400BadRequest);
        }

        await using var transaction = await BeginConfigurationTransactionIfSupportedAsync(cancellationToken);
        var offering = await dbContext.ProgramOfferings
            .Include(item => item.Program).ThenInclude(item => item.Institute)
            .Include(item => item.Applications)
            .Include(item => item.DocumentRequirements)
            .Include(item => item.ExamRequirements).ThenInclude(item => item.Exam)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.ProgramOfferingId == offeringId, cancellationToken);
        if (offering is null)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "İlan bulunamadı.",
                StatusCodes.Status404NotFound);
        }

        if (!UsesDocumentWorkflow(offering))
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "Legacy ilanlar bu düzeltme akışının dışındadır.",
                StatusCodes.Status409Conflict);
        }

        if (offering.IsArchived)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "Arşivlenmiş ilan bu düzeltme akışıyla değiştirilemez.",
                StatusCodes.Status409Conflict);
        }

        if (!offering.IsOpen)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "İlan zaten kapalıdır.",
                StatusCodes.Status409Conflict);
        }

        if (offering.DocumentRequirements.Any(item => item.IsActive && item.IsRequired))
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "İlan artık aktif ve zorunlu belge koşuluna sahiptir. Normal ilan yönetimini kullanın.",
                StatusCodes.Status409Conflict);
        }

        dbContext.Entry(offering).Property(item => item.RowVersion).OriginalValue = rowVersion;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        offering.IsOpen = false;
        offering.UpdatedAtUtc = now;
        AddAudit(adminId, "ProgramOfferingClosedForRemediation", offering, now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "İlan başka bir kullanıcı tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException exception) when (
            !DatabaseExceptionClassifier.IsDeadlock(exception)
            && !DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "İlan güvenli düzeltme için kapatılamadı.",
                StatusCodes.Status409Conflict);
        }

        return ServiceResult<ProgramOfferingAdminDto>.Success(Map(offering));
    }

    private async Task<ServiceResult<ProgramOfferingAdminDto>> UpdateCoreAsync(
        int offeringId,
        int adminId,
        ProgramOfferingUpdateDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginConfigurationTransactionIfSupportedAsync(cancellationToken);
        var offering = await dbContext.ProgramOfferings
            .Include(item => item.Program).ThenInclude(item => item.Institute)
            .Include(item => item.Applications)
            .Include(item => item.DocumentRequirements)
            .Include(item => item.ExamRequirements).ThenInclude(item => item.Exam)
            .Include(item => item.EvaluationCriteria).ThenInclude(item => item.Exam)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.ProgramOfferingId == offeringId, cancellationToken);
        if (offering is null)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure("İlan bulunamadı.", StatusCodes.Status404NotFound);
        }

        var validationError = await ValidateRequestAsync(request, offeringId, cancellationToken);
        if (validationError is not null)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(validationError.Message, validationError.StatusCode);
        }

        var requirementsChanged = !RequirementsAreEquivalent(offering.ExamRequirements, request.ExamRequirements);
        var enablingEvaluationWorkflow = !offering.UsesEvaluationWorkflow && request.UsesEvaluationWorkflow;
        if (offering.UsesEvaluationWorkflow && !request.UsesEvaluationWorkflow)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "Değerlendirme iş akışı etkinleştirildikten sonra kapatılamaz.",
                StatusCodes.Status409Conflict);
        }

        if (offering.Applications.Count > 0 && enablingEvaluationWorkflow)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "Taslak dâhil başvurusu bulunan mevcut ilan değerlendirme iş akışına geçirilemez.",
                StatusCodes.Status409Conflict);
        }

        if (offering.Applications.Count > 0
            && offering.Term != AcademicTerm.LegacyUnspecified
            && (offering.ProgramId != request.ProgramId
                || offering.AcademicYearStart != request.AcademicYearStart
                || offering.Term != request.Term
                || requirementsChanged))
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "Başvurusu bulunan bir ilanın programı, dönemi veya sınav koşulları değiştirilemez.",
                StatusCodes.Status409Conflict);
        }

        if (offering.UsesEvaluationWorkflow
            && offering.EvaluationState != OfferingEvaluationState.Configuring)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "Kesinleştirilmiş veya yayımlanmış bir değerlendirme ilanı değiştirilemez.",
                StatusCodes.Status409Conflict);
        }

        var proposedIsOpen = request.IsOpen && !request.IsArchived;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (request.IsOpen && request.ApplicationDeadlineUtc <= now)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure("Süresi geçmiş bir ilan yeniden açılamaz.", StatusCodes.Status409Conflict);
        }

        if (request.IsOpen
            && !offering.DocumentRequirements.Any(item => item.IsActive && item.IsRequired))
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "İlan açılmadan önce en az bir aktif ve zorunlu belge koşulu tanımlayın.",
                StatusCodes.Status409Conflict);
        }

        if (proposedIsOpen
            && (offering.UsesEvaluationWorkflow || enablingEvaluationWorkflow))
        {
            var policyValidation = EvaluationPolicyInvariant.Validate(
                offering.EvaluationCriteria,
                request.ExamRequirements
                    .Where(item => item.IsRequired)
                    .Select(item => item.ExamId));
            if (!policyValidation.IsValid)
            {
                return ServiceResult<ProgramOfferingAdminDto>.Failure(
                    EvaluationPolicyInvariant.OpeningError(policyValidation),
                    StatusCodes.Status409Conflict);
            }
        }

        byte[] rowVersion;
        try
        {
            rowVersion = Convert.FromBase64String(request.RowVersion);
        }
        catch (FormatException)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure("Eş zamanlılık belirteci geçersiz.", StatusCodes.Status400BadRequest);
        }

        dbContext.Entry(offering).Property(item => item.RowVersion).OriginalValue = rowVersion;
        offering.ProgramId = request.ProgramId;
        offering.AcademicYearStart = request.AcademicYearStart;
        offering.Term = request.Term;
        offering.ApplicationStartUtc = EnsureUtc(request.ApplicationStartUtc);
        offering.ApplicationDeadlineUtc = EnsureUtc(request.ApplicationDeadlineUtc);
        offering.Quota = request.Quota;
        offering.IsOpen = proposedIsOpen;
        offering.IsArchived = request.IsArchived;
        offering.UsesEvaluationWorkflow = offering.UsesEvaluationWorkflow || request.UsesEvaluationWorkflow;
        offering.UpdatedAtUtc = now;
        if (requirementsChanged)
        {
            dbContext.ProgramOfferingExamRequirements.RemoveRange(offering.ExamRequirements);
            offering.ExamRequirements = request.ExamRequirements.Select(MapRequirement).ToList();
        }
        AddAudit(adminId, request.IsArchived ? "ProgramOfferingArchived" : "ProgramOfferingUpdated", offering, now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "İlan başka bir kullanıcı tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException exception) when (
            !DatabaseExceptionClassifier.IsDeadlock(exception)
            && !DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "İlan güncellenemedi; akademik dönem veya sınav koşullarını kontrol edin.",
                StatusCodes.Status409Conflict);
        }

        offering.Program = await dbContext.Programs
            .Include(item => item.Institute)
            .SingleAsync(item => item.ProgramId == offering.ProgramId, cancellationToken);

        foreach (var requirement in offering.ExamRequirements)
        {
            await dbContext.Entry(requirement).Reference(item => item.Exam).LoadAsync(cancellationToken);
        }

        return ServiceResult<ProgramOfferingAdminDto>.Success(Map(offering));
    }

    private async Task<RequestValidationError?> ValidateRequestAsync(
        ProgramOfferingCreateDto request,
        int? currentOfferingId,
        CancellationToken cancellationToken)
    {
        if (request.Term == AcademicTerm.LegacyUnspecified
            || request.AcademicYearStart is < 2000 or > 2200
            || request.Quota <= 0
            || request.ApplicationStartUtc.Kind != DateTimeKind.Utc
            || request.ApplicationDeadlineUtc.Kind != DateTimeKind.Utc
            || request.ApplicationStartUtc >= request.ApplicationDeadlineUtc)
        {
            return InvalidRequest("İlan dönemi, tarih aralığı, saat dilimi veya kontenjanı geçersiz.");
        }

        if (!await dbContext.Programs.AnyAsync(
            item => item.ProgramId == request.ProgramId
                && item.IsActive
                && item.Institute.IsActive,
            cancellationToken))
        {
            return InvalidRequest("Aktif program bulunamadı.");
        }

        if (request.ExamRequirements is null
            || request.ExamRequirements.Any(item =>
                item.ExamId <= 0
                || item.MinimumScore is < 0m or > 999.99m))
        {
            return InvalidRequest("Sınav koşullarından biri geçersiz.");
        }

        if (request.ExamRequirements.GroupBy(item => item.ExamId).Any(group => group.Count() > 1))
        {
            return InvalidRequest("Aynı sınav koşulu birden fazla kez eklenemez.");
        }

        var examIds = request.ExamRequirements.Select(item => item.ExamId).Distinct().ToArray();
        if (examIds.Length > 0
            && await dbContext.Exams.CountAsync(item => examIds.Contains(item.ExamId), cancellationToken) != examIds.Length)
        {
            return InvalidRequest("Sınav koşullarından biri bulunamadı.");
        }

        if (await dbContext.ProgramOfferings.AnyAsync(
            item => item.ProgramOfferingId != currentOfferingId
                && item.ProgramId == request.ProgramId
                && item.AcademicYearStart == request.AcademicYearStart
                && item.Term == request.Term,
            cancellationToken))
        {
            return new RequestValidationError(
                "Aynı program, akademik yıl ve dönem için zaten ilan bulunuyor.",
                StatusCodes.Status409Conflict);
        }

        return null;
    }

    private static RequestValidationError InvalidRequest(string message) =>
        new(message, StatusCodes.Status400BadRequest);

    private static ProgramOfferingExamRequirement MapRequirement(ProgramOfferingRequirementInputDto requirement) => new()
    {
        ExamId = requirement.ExamId,
        MinimumScore = requirement.MinimumScore,
        MinimumValidityDate = requirement.MinimumValidityDate,
        IsRequired = requirement.IsRequired
    };

    private void AddAudit(int adminId, string eventType, ProgramOffering offering, DateTime now) =>
        dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorAdminId = adminId,
            EventType = eventType,
            TargetType = "ProgramOffering",
            TargetId = offering.ProgramOfferingId > 0
                ? offering.ProgramOfferingId.ToString()
                : $"new:{offering.ProgramId}:{offering.AcademicYearStart}:{(int)offering.Term}",
            Details = System.Text.Json.JsonSerializer.Serialize(new
            {
                offering.ProgramId,
                offering.AcademicYearStart,
                offering.Term,
                offering.IsOpen,
                offering.IsArchived,
                offering.UsesEvaluationWorkflow
            }),
            CreatedAtUtc = now
        });

    private static bool RequirementsAreEquivalent(
        IEnumerable<ProgramOfferingExamRequirement> existing,
        IEnumerable<ProgramOfferingRequirementInputDto> requested) =>
        existing
            .OrderBy(item => item.ExamId)
            .Select(item => (item.ExamId, item.MinimumScore, item.MinimumValidityDate, item.IsRequired))
            .SequenceEqual(requested
                .OrderBy(item => item.ExamId)
                .Select(item => (item.ExamId, item.MinimumScore, item.MinimumValidityDate, item.IsRequired)));

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private async Task<IDbContextTransaction?> BeginConfigurationTransactionIfSupportedAsync(
        CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private static ProgramOfferingAdminDto Map(ProgramOffering offering) =>
        Map(
            offering,
            offering.DocumentRequirements.Count,
            offering.DocumentRequirements.Count(requirement => requirement.IsActive && requirement.IsRequired),
            offering.DocumentRequirements.Any(requirement => requirement.IsActive && requirement.IsRequired),
            offering.Applications.Count(application => application.CurrentStatus == DraftStatus),
            offering.Applications.Count(application => application.CurrentStatus != DraftStatus));

    private static ProgramOfferingAdminDto Map(
        ProgramOffering offering,
        int documentRequirementCount,
        int activeRequiredDocumentRequirementCount,
        bool hasActiveRequiredDocumentRequirement,
        int draftApplicationCount,
        int submittedOrLaterApplicationCount) => new(
        offering.ProgramOfferingId,
        offering.ProgramId,
        offering.Program.ProgramName,
        offering.Program.Institute.InstituteName,
        offering.Program.DegreeType,
        offering.AcademicYearStart,
        AcademicPeriodFormatter.FormatAcademicYear(offering.AcademicYearStart),
        offering.Term,
        AcademicPeriodFormatter.FormatTerm(offering.Term),
        offering.ApplicationStartUtc.HasValue ? DateTime.SpecifyKind(offering.ApplicationStartUtc.Value, DateTimeKind.Utc) : null,
        offering.ApplicationDeadlineUtc.HasValue ? DateTime.SpecifyKind(offering.ApplicationDeadlineUtc.Value, DateTimeKind.Utc) : null,
        offering.Quota,
        offering.IsOpen,
        offering.IsArchived,
        UsesDocumentWorkflow(offering),
        offering.UsesEvaluationWorkflow,
        offering.EvaluationState,
        offering.EvaluationFinalizedAtUtc.HasValue
            ? DateTime.SpecifyKind(offering.EvaluationFinalizedAtUtc.Value, DateTimeKind.Utc)
            : null,
        offering.ResultsPublishedAtUtc.HasValue
            ? DateTime.SpecifyKind(offering.ResultsPublishedAtUtc.Value, DateTimeKind.Utc)
            : null,
        documentRequirementCount,
        activeRequiredDocumentRequirementCount,
        hasActiveRequiredDocumentRequirement,
        draftApplicationCount,
        submittedOrLaterApplicationCount,
        ClassifyDocumentConfiguration(
            offering,
            activeRequiredDocumentRequirementCount,
            draftApplicationCount,
            submittedOrLaterApplicationCount),
        Convert.ToBase64String(offering.RowVersion),
        offering.ExamRequirements.Select(requirement => new ExamRequirementDto(
            requirement.ExamId,
            requirement.Exam.ExamName,
            requirement.MinimumScore,
            requirement.MinimumValidityDate,
            requirement.IsRequired)).ToArray());

    private static bool UsesDocumentWorkflow(ProgramOffering offering) =>
        offering.Term != AcademicTerm.LegacyUnspecified;

    private static OfferingDocumentConfigurationHealth ClassifyDocumentConfiguration(
        ProgramOffering offering,
        int activeRequiredDocumentRequirementCount,
        int draftApplicationCount,
        int submittedOrLaterApplicationCount)
    {
        if (!UsesDocumentWorkflow(offering))
        {
            return OfferingDocumentConfigurationHealth.LegacyOutsideDocumentWorkflow;
        }

        if (!offering.IsOpen || offering.IsArchived)
        {
            return OfferingDocumentConfigurationHealth.ClosedWorkflow;
        }

        if (activeRequiredDocumentRequirementCount > 0)
        {
            return OfferingDocumentConfigurationHealth.OpenHealthy;
        }

        if (submittedOrLaterApplicationCount > 0)
        {
            return OfferingDocumentConfigurationHealth.OpenInvalidWithSubmittedApplications;
        }

        return draftApplicationCount > 0
            ? OfferingDocumentConfigurationHealth.OpenInvalidWithDrafts
            : OfferingDocumentConfigurationHealth.OpenInvalidNoApplications;
    }

    private sealed record ProgramOfferingAdminListRow(
        ProgramOffering Offering,
        int DocumentRequirementCount,
        int ActiveRequiredDocumentRequirementCount,
        bool HasActiveRequiredDocumentRequirement,
        int DraftApplicationCount,
        int SubmittedOrLaterApplicationCount);

    private sealed record RequestValidationError(string Message, int StatusCode);
}
