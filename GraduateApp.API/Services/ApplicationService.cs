using System.Data;
using System.Text.Json;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GraduateApp.API.Services;

public interface IApplicationService
{
    Task<ServiceResult<StudentApplicationDto>> CreateAsync(string studentTc, int programOfferingId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudentApplicationDto>> GetForStudentAsync(string studentTc, CancellationToken cancellationToken);
    Task<PagedResult<AdminApplicationListItemDto>> GetForAdminAsync(
        string? search,
        ApplicationStatus? status,
        int? academicYearStart,
        AcademicTerm? term,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
    Task<AdminApplicationDetailDto?> GetDetailForAdminAsync(int applicationId, CancellationToken cancellationToken);
    Task<ServiceResult> UpdateStatusAsync(int applicationId, int adminId, ApplicationStatusUpdateDto request, CancellationToken cancellationToken);
}

public sealed class ApplicationService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider) : IApplicationService
{
    public async Task<ServiceResult<StudentApplicationDto>> CreateAsync(
        string studentTc,
        int programOfferingId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var offering = await dbContext.ProgramOfferings
            .Include(item => item.Program)
            .Include(item => item.ExamRequirements)
                .ThenInclude(item => item.Exam)
            .SingleOrDefaultAsync(item => item.ProgramOfferingId == programOfferingId, cancellationToken);
        if (offering is null)
        {
            return ServiceResult<StudentApplicationDto>.Failure("Dönemsel program ilanı bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (!offering.Program.IsActive
            || offering.IsArchived
            || !offering.IsOpen
            || !offering.ApplicationStartUtc.HasValue
            || !offering.ApplicationDeadlineUtc.HasValue
            || now < offering.ApplicationStartUtc.Value
            || now > offering.ApplicationDeadlineUtc.Value)
        {
            return ServiceResult<StudentApplicationDto>.Failure(
                "Bu ilan şu anda başvuruya açık değil.",
                StatusCodes.Status409Conflict);
        }

        if (offering.Quota <= 0
            || await dbContext.Applications.CountAsync(
                item => item.ProgramOfferingId == programOfferingId
                    && item.CurrentStatus != ApplicationStatus.Withdrawn.ToString(),
                cancellationToken) >= offering.Quota)
        {
            return ServiceResult<StudentApplicationDto>.Failure("İlan kontenjanı dolmuştur.", StatusCodes.Status409Conflict);
        }

        if (await dbContext.Applications.AnyAsync(
            item => item.Tc == studentTc && item.ProgramOfferingId == programOfferingId,
            cancellationToken))
        {
            return ServiceResult<StudentApplicationDto>.Failure(
                "Bu dönemsel ilana daha önce başvuru yapılmış.",
                StatusCodes.Status409Conflict);
        }

        var scores = await dbContext.StudentExamScores
            .Where(item => item.Tc == studentTc)
            .Include(item => item.Exam)
            .ToDictionaryAsync(item => item.ExamId, cancellationToken);
        var snapshots = new List<ApplicationScoreSnapshot>();
        foreach (var requirement in offering.ExamRequirements)
        {
            if (!scores.TryGetValue(requirement.ExamId, out var score))
            {
                if (requirement.IsRequired)
                {
                    return ServiceResult<StudentApplicationDto>.Failure(
                        $"Gerekli {requirement.Exam.ExamName} sınav sonucu bulunamadı.",
                        StatusCodes.Status409Conflict);
                }

                continue;
            }

            var scoreIsValid = score.Score >= requirement.MinimumScore
                && (!requirement.MinimumValidityDate.HasValue
                    || (score.ExamDate.HasValue && score.ExamDate.Value >= requirement.MinimumValidityDate.Value));
            if (!scoreIsValid)
            {
                if (requirement.IsRequired)
                {
                    return ServiceResult<StudentApplicationDto>.Failure(
                        $"{requirement.Exam.ExamName} sınav koşulu sağlanmıyor.",
                        StatusCodes.Status409Conflict);
                }

                continue;
            }

            snapshots.Add(new ApplicationScoreSnapshot
            {
                ExamId = score.ExamId,
                ExamNameSnapshot = score.Exam.ExamName,
                ScoreSnapshot = score.Score,
                ExamDateSnapshot = score.ExamDate,
                CapturedAtUtc = now
            });
        }

        var application = new Application
        {
            Tc = studentTc,
            ProgramOfferingId = programOfferingId,
            ApplicationDate = now,
            CurrentStatus = ApplicationStatus.Pending.ToString(),
            ScoreSnapshots = snapshots
        };
        application.ApplicationStatusHistories.Add(new ApplicationStatusHistory
        {
            PreviousStatus = null,
            StatusName = ApplicationStatus.Pending.ToString(),
            ChangeDate = now,
            Notes = "Başvuru oluşturuldu."
        });
        dbContext.Applications.Add(application);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException)
        {
            return ServiceResult<StudentApplicationDto>.Failure(
                "Başvuru oluşturulamadı; ilan veya sınav koşullarını yeniden kontrol edin.",
                StatusCodes.Status409Conflict);
        }

        return ServiceResult<StudentApplicationDto>.Success(
            MapStudentApplication(application, offering),
            StatusCodes.Status201Created);
    }

    public async Task<IReadOnlyList<StudentApplicationDto>> GetForStudentAsync(
        string studentTc,
        CancellationToken cancellationToken)
    {
        var applications = await dbContext.Applications.AsNoTracking()
            .Where(item => item.Tc == studentTc)
            .Include(item => item.ProgramOffering)
                .ThenInclude(item => item.Program)
            .OrderByDescending(item => item.ApplicationDate)
            .ToListAsync(cancellationToken);

        return applications.Select(item => MapStudentApplication(item, item.ProgramOffering)).ToArray();
    }

    public async Task<PagedResult<AdminApplicationListItemDto>> GetForAdminAsync(
        string? search,
        ApplicationStatus? status,
        int? academicYearStart,
        AcademicTerm? term,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);

        var query = dbContext.Applications.AsNoTracking()
            .Include(item => item.ProgramOffering)
                .ThenInclude(item => item.Program)
            .Include(item => item.TcNavigation)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            query = query.Where(item =>
                item.ProgramOffering.Program.ProgramName.Contains(searchTerm)
                || item.TcNavigation.StudentName.Contains(searchTerm)
                || item.TcNavigation.StudentSurname.Contains(searchTerm)
                || item.TcNavigation.Email.Contains(searchTerm));
        }

        if (status.HasValue)
        {
            var storedStatus = status.Value.ToString();
            query = query.Where(item => item.CurrentStatus == storedStatus);
        }

        if (academicYearStart.HasValue)
        {
            query = query.Where(item => item.ProgramOffering.AcademicYearStart == academicYearStart.Value);
        }

        if (term.HasValue)
        {
            query = query.Where(item => item.ProgramOffering.Term == term.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var applications = await query
            .OrderByDescending(item => item.ApplicationDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = applications.Select(item => new AdminApplicationListItemDto(
            item.ApplicationId,
            $"{item.TcNavigation.StudentName} {item.TcNavigation.StudentSurname}".Trim(),
            MaskTc(item.Tc),
            item.ProgramOffering.Program.ProgramName,
            item.ProgramOffering.AcademicYearStart,
            AcademicPeriodFormatter.FormatAcademicYear(item.ProgramOffering.AcademicYearStart),
            item.ProgramOffering.Term,
            AcademicPeriodFormatter.FormatTerm(item.ProgramOffering.Term),
            DateTime.SpecifyKind(item.ApplicationDate, DateTimeKind.Utc),
            ParseStatus(item.CurrentStatus),
            Convert.ToBase64String(item.RowVersion))).ToArray();

        return new PagedResult<AdminApplicationListItemDto>(items, page, pageSize, totalCount);
    }

    public async Task<AdminApplicationDetailDto?> GetDetailForAdminAsync(
        int applicationId,
        CancellationToken cancellationToken)
    {
        var application = await dbContext.Applications.AsNoTracking()
            .Include(item => item.ProgramOffering).ThenInclude(item => item.Program).ThenInclude(item => item.Institute)
            .Include(item => item.TcNavigation)
            .Include(item => item.ApplicationStatusHistories)
            .Include(item => item.ScoreSnapshots)
            .SingleOrDefaultAsync(item => item.ApplicationId == applicationId, cancellationToken);
        if (application is null)
        {
            return null;
        }

        var history = application.ApplicationStatusHistories
            .OrderByDescending(item => item.ChangeDate)
            .Select(item => new ApplicationStatusHistoryDto(
                TryParseNullableStatus(item.PreviousStatus),
                ParseStatus(item.StatusName),
                DateTime.SpecifyKind(item.ChangeDate, DateTimeKind.Utc),
                item.Notes))
            .ToArray();
        var snapshots = application.ScoreSnapshots
            .OrderBy(item => item.ExamNameSnapshot)
            .Select(item => new ApplicationScoreSnapshotDto(
                item.ExamId,
                item.ExamNameSnapshot,
                item.ScoreSnapshot,
                item.ExamDateSnapshot,
                DateTime.SpecifyKind(item.CapturedAtUtc, DateTimeKind.Utc)))
            .ToArray();
        var offering = application.ProgramOffering;

        return new AdminApplicationDetailDto(
            application.ApplicationId,
            application.Tc,
            $"{application.TcNavigation.StudentName} {application.TcNavigation.StudentSurname}".Trim(),
            application.TcNavigation.Email,
            offering.Program.ProgramName,
            offering.Program.Institute.InstituteName,
            offering.AcademicYearStart,
            AcademicPeriodFormatter.FormatAcademicYear(offering.AcademicYearStart),
            offering.Term,
            AcademicPeriodFormatter.FormatTerm(offering.Term),
            DateTime.SpecifyKind(application.ApplicationDate, DateTimeKind.Utc),
            ParseStatus(application.CurrentStatus),
            Convert.ToBase64String(application.RowVersion),
            history,
            snapshots);
    }

    public async Task<ServiceResult> UpdateStatusAsync(
        int applicationId,
        int adminId,
        ApplicationStatusUpdateDto request,
        CancellationToken cancellationToken)
    {
        var application = await dbContext.Applications.SingleOrDefaultAsync(
            item => item.ApplicationId == applicationId,
            cancellationToken);
        if (application is null)
        {
            return ServiceResult.Failure("Başvuru bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (!ApplicationStatusRules.TryParseStoredValue(application.CurrentStatus, out var currentStatus)
            || !ApplicationStatusRules.CanTransition(currentStatus, request.NewStatus))
        {
            return ServiceResult.Failure("Bu durum geçişine izin verilmiyor.", StatusCodes.Status409Conflict);
        }

        byte[] rowVersion;
        try
        {
            rowVersion = Convert.FromBase64String(request.RowVersion);
        }
        catch (FormatException)
        {
            return ServiceResult.Failure("Eş zamanlılık belirteci geçersiz.", StatusCodes.Status400BadRequest);
        }

        dbContext.Entry(application).Property(item => item.RowVersion).OriginalValue = rowVersion;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        application.CurrentStatus = request.NewStatus.ToString();
        dbContext.ApplicationStatusHistories.Add(new ApplicationStatusHistory
        {
            ApplicationId = application.ApplicationId,
            PreviousStatus = currentStatus.ToString(),
            StatusName = request.NewStatus.ToString(),
            ChangedByAdminId = adminId,
            ChangeDate = now,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        });
        dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorAdminId = adminId,
            EventType = "ApplicationStatusChanged",
            TargetType = "Application",
            TargetId = application.ApplicationId.ToString(),
            Details = JsonSerializer.Serialize(new { Previous = currentStatus, Current = request.NewStatus }),
            CreatedAtUtc = now
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Failure(
                "Başvuru başka bir kullanıcı tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private static StudentApplicationDto MapStudentApplication(Application application, ProgramOffering offering) =>
        new(
            application.ApplicationId,
            offering.ProgramOfferingId,
            offering.ProgramId,
            offering.Program.ProgramName,
            offering.AcademicYearStart,
            AcademicPeriodFormatter.FormatAcademicYear(offering.AcademicYearStart),
            offering.Term,
            AcademicPeriodFormatter.FormatTerm(offering.Term),
            DateTime.SpecifyKind(application.ApplicationDate, DateTimeKind.Utc),
            ParseStatus(application.CurrentStatus),
            Convert.ToBase64String(application.RowVersion));

    private static ApplicationStatus ParseStatus(string value) =>
        ApplicationStatusRules.TryParseStoredValue(value, out var status) ? status : ApplicationStatus.Pending;

    private static ApplicationStatus? TryParseNullableStatus(string? value) =>
        ApplicationStatusRules.TryParseStoredValue(value, out var status) ? status : null;

    private static string MaskTc(string tc) => tc.Length >= 4 ? $"*******{tc[^4..]}" : "***********";
}
