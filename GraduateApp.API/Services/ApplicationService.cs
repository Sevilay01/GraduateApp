using System.Text.Json;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Services;

public interface IApplicationService
{
    Task<ServiceResult<StudentApplicationDto>> CreateAsync(string studentTc, int programId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudentApplicationDto>> GetForStudentAsync(string studentTc, CancellationToken cancellationToken);
    Task<PagedResult<AdminApplicationListItemDto>> GetForAdminAsync(string? search, ApplicationStatus? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<AdminApplicationDetailDto?> GetDetailForAdminAsync(int applicationId, CancellationToken cancellationToken);
    Task<ServiceResult> UpdateStatusAsync(int applicationId, int adminId, ApplicationStatusUpdateDto request, CancellationToken cancellationToken);
}

public sealed class ApplicationService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider) : IApplicationService
{
    public async Task<ServiceResult<StudentApplicationDto>> CreateAsync(
        string studentTc,
        int programId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var program = await dbContext.Programs.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProgramId == programId, cancellationToken);
        if (program is null)
        {
            return ServiceResult<StudentApplicationDto>.Failure("Program bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (!program.IsOpen || (program.ApplicationDeadlineUtc.HasValue && program.ApplicationDeadlineUtc.Value < now))
        {
            return ServiceResult<StudentApplicationDto>.Failure(
                "Bu program başvuruya açık değil veya son başvuru tarihi geçmiş.",
                StatusCodes.Status409Conflict);
        }

        if (await dbContext.Applications.AnyAsync(
            item => item.Tc == studentTc && item.ProgramId == programId,
            cancellationToken))
        {
            return ServiceResult<StudentApplicationDto>.Failure(
                "Bu programa daha önce başvuru yapılmış.",
                StatusCodes.Status409Conflict);
        }

        var application = new Application
        {
            Tc = studentTc,
            ProgramId = programId,
            ApplicationDate = now,
            CurrentStatus = ApplicationStatus.Pending.ToString()
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
        }
        catch (DbUpdateException)
        {
            return ServiceResult<StudentApplicationDto>.Failure(
                "Bu programa daha önce başvuru yapılmış.",
                StatusCodes.Status409Conflict);
        }

        return ServiceResult<StudentApplicationDto>.Success(
            MapStudentApplication(application, program.ProgramName),
            StatusCodes.Status201Created);
    }

    public async Task<IReadOnlyList<StudentApplicationDto>> GetForStudentAsync(
        string studentTc,
        CancellationToken cancellationToken)
    {
        var applications = await dbContext.Applications.AsNoTracking()
            .Where(item => item.Tc == studentTc)
            .Include(item => item.Program)
            .OrderByDescending(item => item.ApplicationDate)
            .ToListAsync(cancellationToken);

        return applications.Select(item => MapStudentApplication(item, item.Program.ProgramName)).ToArray();
    }

    public async Task<PagedResult<AdminApplicationListItemDto>> GetForAdminAsync(
        string? search,
        ApplicationStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);

        var query = dbContext.Applications.AsNoTracking()
            .Include(item => item.Program)
            .Include(item => item.TcNavigation)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(item =>
                item.Program.ProgramName.Contains(term)
                || item.TcNavigation.StudentName.Contains(term)
                || item.TcNavigation.StudentSurname.Contains(term)
                || item.TcNavigation.Email.Contains(term));
        }

        if (status.HasValue)
        {
            var storedStatus = status.Value.ToString();
            query = query.Where(item => item.CurrentStatus == storedStatus);
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
            item.Program.ProgramName,
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
            .Include(item => item.Program).ThenInclude(item => item.Institute)
            .Include(item => item.TcNavigation)
            .Include(item => item.ApplicationStatusHistories)
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

        return new AdminApplicationDetailDto(
            application.ApplicationId,
            application.Tc,
            $"{application.TcNavigation.StudentName} {application.TcNavigation.StudentSurname}".Trim(),
            application.TcNavigation.Email,
            application.Program.ProgramName,
            application.Program.Institute.InstituteName,
            DateTime.SpecifyKind(application.ApplicationDate, DateTimeKind.Utc),
            ParseStatus(application.CurrentStatus),
            Convert.ToBase64String(application.RowVersion),
            history);
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
            return ServiceResult.Failure(
                "Bu durum geçişine izin verilmiyor.",
                StatusCodes.Status409Conflict);
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

    private static StudentApplicationDto MapStudentApplication(Application application, string programName) =>
        new(
            application.ApplicationId,
            application.ProgramId,
            programName,
            DateTime.SpecifyKind(application.ApplicationDate, DateTimeKind.Utc),
            ParseStatus(application.CurrentStatus),
            Convert.ToBase64String(application.RowVersion));

    private static ApplicationStatus ParseStatus(string value) =>
        ApplicationStatusRules.TryParseStoredValue(value, out var status) ? status : ApplicationStatus.Pending;

    private static ApplicationStatus? TryParseNullableStatus(string? value) =>
        ApplicationStatusRules.TryParseStoredValue(value, out var status) ? status : null;

    private static string MaskTc(string tc) => tc.Length >= 4 ? $"*******{tc[^4..]}" : "***********";
}
