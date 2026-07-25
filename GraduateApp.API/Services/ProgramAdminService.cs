using System.Text.Json;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Services;

public interface IProgramAdminService
{
    Task<PagedResult<ProgramAdminDto>> GetAsync(
        string? search,
        bool? isActive,
        int? instituteId,
        string? degreeType,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
    Task<ProgramAdminDto?> GetByIdAsync(int programId, CancellationToken cancellationToken);
    Task<ServiceResult<ProgramAdminDto>> CreateAsync(
        int adminId,
        ProgramCreateDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult<ProgramAdminDto>> UpdateAsync(
        int programId,
        int adminId,
        ProgramUpdateDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult<ProgramAdminDto>> SetActiveAsync(
        int programId,
        int adminId,
        bool isActive,
        CatalogConcurrencyDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult> DeleteAsync(
        int programId,
        int adminId,
        string rowVersion,
        CancellationToken cancellationToken);
}

public sealed class ProgramAdminService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider) : IProgramAdminService
{
    public async Task<PagedResult<ProgramAdminDto>> GetAsync(
        string? search,
        bool? isActive,
        int? instituteId,
        string? degreeType,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var query = dbContext.Programs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(item => item.ProgramName.Contains(term));
        }

        if (isActive.HasValue)
        {
            query = query.Where(item => item.IsActive == isActive.Value);
        }

        if (instituteId.HasValue)
        {
            query = query.Where(item => item.InstituteId == instituteId.Value);
        }

        if (!string.IsNullOrWhiteSpace(degreeType))
        {
            query = query.Where(item => item.DegreeType == degreeType);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(item => item.Institute.InstituteName)
            .ThenBy(item => item.ProgramName)
            .ThenBy(item => item.DegreeType)
            .ThenBy(item => item.ProgramId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new
            {
                item.ProgramId,
                item.InstituteId,
                item.Institute.InstituteName,
                InstituteIsActive = item.Institute.IsActive,
                item.ProgramName,
                item.DegreeType,
                item.IsActive,
                item.CreatedAtUtc,
                item.UpdatedAtUtc,
                item.RowVersion,
                OfferingCount = item.Offerings.Count
            })
            .ToListAsync(cancellationToken);
        var items = rows.Select(item => new ProgramAdminDto(
                item.ProgramId,
                item.InstituteId,
                item.InstituteName,
                item.ProgramName,
                item.DegreeType,
                item.IsActive,
                item.IsActive && item.InstituteIsActive,
                item.CreatedAtUtc,
                item.UpdatedAtUtc,
                Convert.ToBase64String(item.RowVersion),
                item.OfferingCount))
            .ToArray();
        return new PagedResult<ProgramAdminDto>(items, page, pageSize, totalCount);
    }

    public async Task<ProgramAdminDto?> GetByIdAsync(int programId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Programs.AsNoTracking()
            .Where(item => item.ProgramId == programId)
            .Select(item => new
            {
                item.ProgramId,
                item.InstituteId,
                item.Institute.InstituteName,
                InstituteIsActive = item.Institute.IsActive,
                item.ProgramName,
                item.DegreeType,
                item.IsActive,
                item.CreatedAtUtc,
                item.UpdatedAtUtc,
                item.RowVersion,
                OfferingCount = item.Offerings.Count
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new ProgramAdminDto(
                row.ProgramId,
                row.InstituteId,
                row.InstituteName,
                row.ProgramName,
                row.DegreeType,
                row.IsActive,
                row.IsActive && row.InstituteIsActive,
                row.CreatedAtUtc,
                row.UpdatedAtUtc,
                Convert.ToBase64String(row.RowVersion),
                row.OfferingCount);
    }

    public async Task<ServiceResult<ProgramAdminDto>> CreateAsync(
        int adminId,
        ProgramCreateDto request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateRequestAsync(request, null, cancellationToken);
        if (!validation.IsSuccess)
        {
            return ServiceResult<ProgramAdminDto>.Failure(validation.Error!, validation.StatusCode);
        }

        var normalized = validation.Value!;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var program = new GraduateApp.API.Models.Program
        {
            InstituteId = request.InstituteId,
            ProgramName = normalized.ProgramName,
            DegreeType = normalized.DegreeType,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Programs.Add(program);
        AddAudit(adminId, "ProgramCreated", $"new:{request.InstituteId}", program, now);
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
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult<ProgramAdminDto>.Failure(
                "Program geçerli bir enstitüye bağlanamadı.",
                StatusCodes.Status400BadRequest);
        }

        var created = await GetByIdAsync(program.ProgramId, cancellationToken);
        return ServiceResult<ProgramAdminDto>.Success(created!, StatusCodes.Status201Created);
    }

    public async Task<ServiceResult<ProgramAdminDto>> UpdateAsync(
        int programId,
        int adminId,
        ProgramUpdateDto request,
        CancellationToken cancellationToken)
    {
        var program = await dbContext.Programs.SingleOrDefaultAsync(
            item => item.ProgramId == programId,
            cancellationToken);
        if (program is null)
        {
            return NotFoundFailure();
        }

        var validation = await ValidateRequestAsync(request, programId, cancellationToken);
        if (!validation.IsSuccess)
        {
            return ServiceResult<ProgramAdminDto>.Failure(validation.Error!, validation.StatusCode);
        }

        if (!TrySetOriginalRowVersion(program, request.RowVersion, out var rowVersionError))
        {
            return ServiceResult<ProgramAdminDto>.Failure(rowVersionError!, StatusCodes.Status400BadRequest);
        }

        var normalized = validation.Value!;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        program.InstituteId = request.InstituteId;
        program.ProgramName = normalized.ProgramName;
        program.DegreeType = normalized.DegreeType;
        program.UpdatedAtUtc = now;
        AddAudit(adminId, "ProgramUpdated", programId.ToString(), program, now);
        var saveResult = await SaveAsync(cancellationToken);
        if (!saveResult.IsSuccess)
        {
            return ServiceResult<ProgramAdminDto>.Failure(saveResult.Error!, saveResult.StatusCode);
        }

        var updated = await GetByIdAsync(programId, cancellationToken);
        return ServiceResult<ProgramAdminDto>.Success(updated!);
    }

    public async Task<ServiceResult<ProgramAdminDto>> SetActiveAsync(
        int programId,
        int adminId,
        bool isActive,
        CatalogConcurrencyDto request,
        CancellationToken cancellationToken)
    {
        var program = await dbContext.Programs.SingleOrDefaultAsync(
            item => item.ProgramId == programId,
            cancellationToken);
        if (program is null)
        {
            return NotFoundFailure();
        }

        if (program.IsActive == isActive)
        {
            return ServiceResult<ProgramAdminDto>.Failure(
                isActive ? "Program zaten aktif." : "Program zaten pasif.",
                StatusCodes.Status409Conflict);
        }

        if (!TrySetOriginalRowVersion(program, request.RowVersion, out var rowVersionError))
        {
            return ServiceResult<ProgramAdminDto>.Failure(rowVersionError!, StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        program.IsActive = isActive;
        program.UpdatedAtUtc = now;
        AddAudit(adminId, isActive ? "ProgramActivated" : "ProgramDeactivated", programId.ToString(), program, now);
        var saveResult = await SaveAsync(cancellationToken);
        if (!saveResult.IsSuccess)
        {
            return ServiceResult<ProgramAdminDto>.Failure(saveResult.Error!, saveResult.StatusCode);
        }

        var updated = await GetByIdAsync(programId, cancellationToken);
        return ServiceResult<ProgramAdminDto>.Success(updated!);
    }

    public async Task<ServiceResult> DeleteAsync(
        int programId,
        int adminId,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var program = await dbContext.Programs.SingleOrDefaultAsync(
            item => item.ProgramId == programId,
            cancellationToken);
        if (program is null)
        {
            return ServiceResult.Failure("Program bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (await dbContext.ProgramOfferings.AnyAsync(item => item.ProgramId == programId, cancellationToken))
        {
            return ServiceResult.Failure(
                "Dönemsel ilanı bulunan program silinemez. Tarihsel ilan ve başvuruları korumak için programı pasifleştirin.",
                StatusCodes.Status409Conflict);
        }

        if (!TrySetOriginalRowVersion(program, rowVersion, out var rowVersionError))
        {
            return ServiceResult.Failure(rowVersionError!, StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        dbContext.Programs.Remove(program);
        AddAudit(adminId, "ProgramDeleted", programId.ToString(), program, now);
        var saveResult = await SaveAsync(cancellationToken);
        return saveResult.IsSuccess ? ServiceResult.Success() : saveResult;
    }

    private async Task<ServiceResult<NormalizedProgramInput>> ValidateRequestAsync(
        ProgramCreateDto request,
        int? currentProgramId,
        CancellationToken cancellationToken)
    {
        var programName = request.ProgramName?.Trim() ?? string.Empty;
        if (programName.Length is < 2 or > 100)
        {
            return ServiceResult<NormalizedProgramInput>.Failure(
                "Program adı 2 ile 100 karakter arasında ve boşluksuz biçimde girilmelidir.",
                StatusCodes.Status400BadRequest);
        }

        if (!DegreeTypeCatalog.TryCanonicalize(request.DegreeType, out var degreeType))
        {
            return ServiceResult<NormalizedProgramInput>.Failure(
                "Derece türü desteklenen değerlerden biri olmalıdır.",
                StatusCodes.Status400BadRequest);
        }

        if (!await dbContext.Institutes.AnyAsync(
            item => item.InstituteId == request.InstituteId,
            cancellationToken))
        {
            return ServiceResult<NormalizedProgramInput>.Failure(
                "Seçilen enstitü bulunamadı.",
                StatusCodes.Status400BadRequest);
        }

        if (await dbContext.Programs.AnyAsync(
            item => item.ProgramId != currentProgramId
                && item.InstituteId == request.InstituteId
                && item.ProgramName == programName
                && item.DegreeType == degreeType,
            cancellationToken))
        {
            return ServiceResult<NormalizedProgramInput>.Failure(
                "Aynı enstitü, program adı ve derece türü için zaten kayıt bulunuyor.",
                StatusCodes.Status409Conflict);
        }

        return ServiceResult<NormalizedProgramInput>.Success(new(programName, degreeType));
    }

    private bool TrySetOriginalRowVersion(
        GraduateApp.API.Models.Program program,
        string encodedRowVersion,
        out string? error)
    {
        if (!TryDecodeRowVersion(encodedRowVersion, out var rowVersion))
        {
            error = "Program eşzamanlılık bilgisi geçersiz.";
            return false;
        }

        dbContext.Entry(program).Property(item => item.RowVersion).OriginalValue = rowVersion;
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
                "Aynı enstitü, program adı ve derece türü için zaten kayıt bulunuyor.",
                StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult.Failure(
                "Program bağlı kayıtlar veya veri bütünlüğü nedeniyle değiştirilemedi.",
                StatusCodes.Status409Conflict);
        }
    }

    private void AddAudit(
        int adminId,
        string eventType,
        string targetId,
        GraduateApp.API.Models.Program program,
        DateTime now) =>
        dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorAdminId = adminId,
            EventType = eventType,
            TargetType = "Program",
            TargetId = targetId,
            Details = JsonSerializer.Serialize(new
            {
                program.InstituteId,
                program.DegreeType,
                program.IsActive
            }),
            CreatedAtUtc = now
        });

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

    private static ServiceResult<ProgramAdminDto> DuplicateFailure() =>
        ServiceResult<ProgramAdminDto>.Failure(
            "Aynı enstitü, program adı ve derece türü için zaten kayıt bulunuyor.",
            StatusCodes.Status409Conflict);

    private static ServiceResult<ProgramAdminDto> NotFoundFailure() =>
        ServiceResult<ProgramAdminDto>.Failure("Program bulunamadı.", StatusCodes.Status404NotFound);

    private sealed record NormalizedProgramInput(string ProgramName, string DegreeType);
}
