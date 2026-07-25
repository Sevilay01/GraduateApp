using System.Text.Json;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Services;

public interface IInstituteAdminService
{
    Task<PagedResult<InstituteAdminDto>> GetAsync(
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
    Task<InstituteAdminDto?> GetByIdAsync(int instituteId, CancellationToken cancellationToken);
    Task<ServiceResult<InstituteAdminDto>> CreateAsync(
        int adminId,
        InstituteCreateDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult<InstituteAdminDto>> UpdateAsync(
        int instituteId,
        int adminId,
        InstituteUpdateDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult<InstituteAdminDto>> SetActiveAsync(
        int instituteId,
        int adminId,
        bool isActive,
        CatalogConcurrencyDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult> DeleteAsync(
        int instituteId,
        int adminId,
        string rowVersion,
        CancellationToken cancellationToken);
}

public sealed class InstituteAdminService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider) : IInstituteAdminService
{
    public async Task<PagedResult<InstituteAdminDto>> GetAsync(
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var query = dbContext.Institutes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(item => item.InstituteName.Contains(term));
        }

        if (isActive.HasValue)
        {
            query = query.Where(item => item.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(item => item.InstituteName)
            .ThenBy(item => item.InstituteId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new
            {
                item.InstituteId,
                item.InstituteName,
                item.IsActive,
                item.CreatedAtUtc,
                item.UpdatedAtUtc,
                item.RowVersion,
                ProgramCount = item.Programs.Count
            })
            .ToListAsync(cancellationToken);
        var items = rows.Select(item => new InstituteAdminDto(
                item.InstituteId,
                item.InstituteName,
                item.IsActive,
                item.CreatedAtUtc,
                item.UpdatedAtUtc,
                Convert.ToBase64String(item.RowVersion),
                item.ProgramCount))
            .ToArray();
        return new PagedResult<InstituteAdminDto>(items, page, pageSize, totalCount);
    }

    public async Task<InstituteAdminDto?> GetByIdAsync(
        int instituteId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.Institutes.AsNoTracking()
            .Where(item => item.InstituteId == instituteId)
            .Select(item => new
            {
                item.InstituteId,
                item.InstituteName,
                item.IsActive,
                item.CreatedAtUtc,
                item.UpdatedAtUtc,
                item.RowVersion,
                ProgramCount = item.Programs.Count
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new InstituteAdminDto(
                row.InstituteId,
                row.InstituteName,
                row.IsActive,
                row.CreatedAtUtc,
                row.UpdatedAtUtc,
                Convert.ToBase64String(row.RowVersion),
                row.ProgramCount);
    }

    public async Task<ServiceResult<InstituteAdminDto>> CreateAsync(
        int adminId,
        InstituteCreateDto request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateName(request.InstituteName);
        if (!validation.IsSuccess)
        {
            return ServiceResult<InstituteAdminDto>.Failure(validation.Error!, validation.StatusCode);
        }

        var name = validation.Value!;
        if (await dbContext.Institutes.AnyAsync(
            item => item.InstituteName == name,
            cancellationToken))
        {
            return DuplicateFailure();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var institute = new Institute
        {
            InstituteName = name,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Institutes.Add(institute);
        AddAudit(adminId, "InstituteCreated", "new", new { institute.IsActive }, now);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            DatabaseExceptionClassifier.IsUniqueConstraintViolation(exception)
            && !DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return DuplicateFailure();
        }

        var created = await GetByIdAsync(institute.InstituteId, cancellationToken);
        return ServiceResult<InstituteAdminDto>.Success(created!, StatusCodes.Status201Created);
    }

    public async Task<ServiceResult<InstituteAdminDto>> UpdateAsync(
        int instituteId,
        int adminId,
        InstituteUpdateDto request,
        CancellationToken cancellationToken)
    {
        var institute = await dbContext.Institutes.SingleOrDefaultAsync(
            item => item.InstituteId == instituteId,
            cancellationToken);
        if (institute is null)
        {
            return NotFoundFailure();
        }

        var validation = ValidateName(request.InstituteName);
        if (!validation.IsSuccess)
        {
            return ServiceResult<InstituteAdminDto>.Failure(validation.Error!, validation.StatusCode);
        }

        if (!TrySetOriginalRowVersion(institute, request.RowVersion, out var rowVersionError))
        {
            return ServiceResult<InstituteAdminDto>.Failure(rowVersionError!, StatusCodes.Status400BadRequest);
        }

        var name = validation.Value!;
        if (await dbContext.Institutes.AnyAsync(
            item => item.InstituteId != instituteId && item.InstituteName == name,
            cancellationToken))
        {
            return DuplicateFailure();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        institute.InstituteName = name;
        institute.UpdatedAtUtc = now;
        AddAudit(adminId, "InstituteUpdated", instituteId.ToString(), new { institute.IsActive }, now);
        var saveResult = await SaveAsync(cancellationToken);
        if (!saveResult.IsSuccess)
        {
            return ServiceResult<InstituteAdminDto>.Failure(saveResult.Error!, saveResult.StatusCode);
        }

        var updated = await GetByIdAsync(instituteId, cancellationToken);
        return ServiceResult<InstituteAdminDto>.Success(updated!);
    }

    public async Task<ServiceResult<InstituteAdminDto>> SetActiveAsync(
        int instituteId,
        int adminId,
        bool isActive,
        CatalogConcurrencyDto request,
        CancellationToken cancellationToken)
    {
        var institute = await dbContext.Institutes.SingleOrDefaultAsync(
            item => item.InstituteId == instituteId,
            cancellationToken);
        if (institute is null)
        {
            return NotFoundFailure();
        }

        if (institute.IsActive == isActive)
        {
            return ServiceResult<InstituteAdminDto>.Failure(
                isActive ? "Enstitü zaten aktif." : "Enstitü zaten pasif.",
                StatusCodes.Status409Conflict);
        }

        if (!TrySetOriginalRowVersion(institute, request.RowVersion, out var rowVersionError))
        {
            return ServiceResult<InstituteAdminDto>.Failure(rowVersionError!, StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        institute.IsActive = isActive;
        institute.UpdatedAtUtc = now;
        AddAudit(
            adminId,
            isActive ? "InstituteActivated" : "InstituteDeactivated",
            instituteId.ToString(),
            new { institute.IsActive },
            now);
        var saveResult = await SaveAsync(cancellationToken);
        if (!saveResult.IsSuccess)
        {
            return ServiceResult<InstituteAdminDto>.Failure(saveResult.Error!, saveResult.StatusCode);
        }

        var updated = await GetByIdAsync(instituteId, cancellationToken);
        return ServiceResult<InstituteAdminDto>.Success(updated!);
    }

    public async Task<ServiceResult> DeleteAsync(
        int instituteId,
        int adminId,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var institute = await dbContext.Institutes.SingleOrDefaultAsync(
            item => item.InstituteId == instituteId,
            cancellationToken);
        if (institute is null)
        {
            return ServiceResult.Failure("Enstitü bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (await dbContext.Programs.AnyAsync(item => item.InstituteId == instituteId, cancellationToken))
        {
            return ServiceResult.Failure(
                "Bağlı programı bulunan enstitü silinemez. Tarihsel kayıtları korumak için enstitüyü pasifleştirin.",
                StatusCodes.Status409Conflict);
        }

        if (!TrySetOriginalRowVersion(institute, rowVersion, out var rowVersionError))
        {
            return ServiceResult.Failure(rowVersionError!, StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        dbContext.Institutes.Remove(institute);
        AddAudit(adminId, "InstituteDeleted", instituteId.ToString(), new { }, now);
        var saveResult = await SaveAsync(cancellationToken);
        return saveResult.IsSuccess ? ServiceResult.Success() : saveResult;
    }

    private bool TrySetOriginalRowVersion(
        Institute institute,
        string encodedRowVersion,
        out string? error)
    {
        if (!TryDecodeRowVersion(encodedRowVersion, out var rowVersion))
        {
            error = "Enstitü eşzamanlılık bilgisi geçersiz.";
            return false;
        }

        dbContext.Entry(institute).Property(item => item.RowVersion).OriginalValue = rowVersion;
        error = null;
        return true;
    }

    private async Task<ServiceResult> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Failure(
                "Kayıt başka bir kullanıcı tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException exception) when (
            DatabaseExceptionClassifier.IsUniqueConstraintViolation(exception)
            && !DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult.Failure(
                "Aynı adda bir enstitü zaten bulunuyor.",
                StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult.Failure(
                "Enstitü bağlı kayıtlar veya veri bütünlüğü nedeniyle değiştirilemedi.",
                StatusCodes.Status409Conflict);
        }
    }

    private void AddAudit(int adminId, string eventType, string targetId, object details, DateTime now) =>
        dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorAdminId = adminId,
            EventType = eventType,
            TargetType = "Institute",
            TargetId = targetId,
            Details = JsonSerializer.Serialize(details),
            CreatedAtUtc = now
        });

    private static ServiceResult<string> ValidateName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        return name.Length is < 2 or > 100
            ? ServiceResult<string>.Failure(
                "Enstitü adı 2 ile 100 karakter arasında ve boşluksuz biçimde girilmelidir.",
                StatusCodes.Status400BadRequest)
            : ServiceResult<string>.Success(name);
    }

    private static bool TryDecodeRowVersion(string? value, out byte[] rowVersion)
    {
        try
        {
            rowVersion = Convert.FromBase64String(value ?? string.Empty);
            return rowVersion.Length == 8;
        }
        catch (FormatException)
        {
            rowVersion = [];
            return false;
        }
    }

    private static ServiceResult<InstituteAdminDto> DuplicateFailure() =>
        ServiceResult<InstituteAdminDto>.Failure(
            "Aynı adda bir enstitü zaten bulunuyor.",
            StatusCodes.Status409Conflict);

    private static ServiceResult<InstituteAdminDto> NotFoundFailure() =>
        ServiceResult<InstituteAdminDto>.Failure("Enstitü bulunamadı.", StatusCodes.Status404NotFound);
}
