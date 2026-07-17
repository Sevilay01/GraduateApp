using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;

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
}

public sealed class ProgramOfferingService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider) : IProgramOfferingService
{
    public async Task<IReadOnlyList<ProgramOfferingAdminDto>> GetForAdminAsync(
        int? academicYearStart,
        AcademicTerm? term,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ProgramOfferings.AsNoTracking()
            .Include(item => item.Program)
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

        var offerings = await query
            .OrderByDescending(item => item.AcademicYearStart)
            .ThenBy(item => item.Term)
            .ThenBy(item => item.Program.ProgramName)
            .ToListAsync(cancellationToken);
        return offerings.Select(Map).ToArray();
    }

    public async Task<ProgramOfferingCatalogDto> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var programs = await dbContext.Programs.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.ProgramName)
            .Select(item => new ProgramCatalogItemDto(
                item.ProgramId,
                item.ProgramName,
                item.Institute.InstituteName))
            .ToListAsync(cancellationToken);
        var exams = await dbContext.Exams.AsNoTracking()
            .OrderBy(item => item.ExamName)
            .Select(item => new ExamCatalogItemDto(item.ExamId, item.ExamName))
            .ToListAsync(cancellationToken);
        return new ProgramOfferingCatalogDto(programs, exams);
    }

    public async Task<ServiceResult<ProgramOfferingAdminDto>> CreateAsync(
        int adminId,
        ProgramOfferingCreateDto request,
        CancellationToken cancellationToken)
    {
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
            IsOpen = request.IsOpen,
            IsArchived = false,
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
        catch (DbUpdateException)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "Aynı program, akademik yıl ve dönem için zaten ilan bulunuyor.",
                StatusCodes.Status409Conflict);
        }

        await dbContext.Entry(offering).Reference(item => item.Program).LoadAsync(cancellationToken);
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
        var offering = await dbContext.ProgramOfferings
            .Include(item => item.Program)
            .Include(item => item.Applications)
            .Include(item => item.ExamRequirements).ThenInclude(item => item.Exam)
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

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (request.IsOpen && request.ApplicationDeadlineUtc <= now)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure("Süresi geçmiş bir ilan yeniden açılamaz.", StatusCodes.Status409Conflict);
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
        offering.IsOpen = request.IsOpen && !request.IsArchived;
        offering.IsArchived = request.IsArchived;
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
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "İlan başka bir kullanıcı tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException)
        {
            return ServiceResult<ProgramOfferingAdminDto>.Failure(
                "İlan güncellenemedi; akademik dönem veya sınav koşullarını kontrol edin.",
                StatusCodes.Status409Conflict);
        }

        dbContext.Entry(offering).Reference(item => item.Program).IsLoaded = false;
        await dbContext.Entry(offering).Reference(item => item.Program).LoadAsync(cancellationToken);

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
            item => item.ProgramId == request.ProgramId && item.IsActive,
            cancellationToken))
        {
            return InvalidRequest("Aktif program bulunamadı.");
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
                offering.IsArchived
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

    private static ProgramOfferingAdminDto Map(ProgramOffering offering) => new(
        offering.ProgramOfferingId,
        offering.ProgramId,
        offering.Program.ProgramName,
        offering.AcademicYearStart,
        AcademicPeriodFormatter.FormatAcademicYear(offering.AcademicYearStart),
        offering.Term,
        AcademicPeriodFormatter.FormatTerm(offering.Term),
        offering.ApplicationStartUtc.HasValue ? DateTime.SpecifyKind(offering.ApplicationStartUtc.Value, DateTimeKind.Utc) : null,
        offering.ApplicationDeadlineUtc.HasValue ? DateTime.SpecifyKind(offering.ApplicationDeadlineUtc.Value, DateTimeKind.Utc) : null,
        offering.Quota,
        offering.IsOpen,
        offering.IsArchived,
        Convert.ToBase64String(offering.RowVersion),
        offering.ExamRequirements.Select(requirement => new ExamRequirementDto(
            requirement.ExamId,
            requirement.Exam.ExamName,
            requirement.MinimumScore,
            requirement.MinimumValidityDate,
            requirement.IsRequired)).ToArray());

    private sealed record RequestValidationError(string Message, int StatusCode);
}
