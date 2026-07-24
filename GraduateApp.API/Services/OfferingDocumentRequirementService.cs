using System.Data;
using System.Text.Json;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace GraduateApp.API.Services;

public interface IOfferingDocumentRequirementService
{
    Task<IReadOnlyList<OfferingDocumentRequirementDto>> GetAsync(int offeringId, CancellationToken cancellationToken);
    Task<ServiceResult<OfferingDocumentRequirementDto>> CreateAsync(
        int offeringId,
        int adminId,
        OfferingDocumentRequirementCreateDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult<OfferingDocumentRequirementDto>> UpdateAsync(
        int offeringId,
        Guid publicId,
        int adminId,
        OfferingDocumentRequirementUpdateDto request,
        CancellationToken cancellationToken);
    Task<ServiceResult<OfferingDocumentRequirementDto>> SetActiveAsync(
        int offeringId,
        Guid publicId,
        int adminId,
        DocumentRequirementActiveDto request,
        CancellationToken cancellationToken);
}

public sealed class OfferingDocumentRequirementService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<DocumentUploadOptions> uploadOptions) : IOfferingDocumentRequirementService
{
    public async Task<IReadOnlyList<OfferingDocumentRequirementDto>> GetAsync(
        int offeringId,
        CancellationToken cancellationToken) =>
        (await dbContext.ProgramOfferingDocumentRequirements.AsNoTracking()
            .Where(item => item.ProgramOfferingId == offeringId)
            .OrderBy(item => item.DisplayName)
            .ThenBy(item => item.RequirementId)
            .ToListAsync(cancellationToken))
        .Select(Map)
        .ToArray();

    public async Task<ServiceResult<OfferingDocumentRequirementDto>> CreateAsync(
        int offeringId,
        int adminId,
        OfferingDocumentRequirementCreateDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CreateCoreAsync(offeringId, adminId, request, cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            && IsSqlServerDeadlock(exception))
        {
            return ConfigurationConcurrencyConflict();
        }
    }

    private async Task<ServiceResult<OfferingDocumentRequirementDto>> CreateCoreAsync(
        int offeringId,
        int adminId,
        OfferingDocumentRequirementCreateDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginConfigurationTransactionIfSupportedAsync(cancellationToken);
        var offering = await LoadOfferingAggregateAsync(offeringId, cancellationToken);
        if (offering is null)
        {
            return ServiceResult<OfferingDocumentRequirementDto>.Failure("İlan bulunamadı.", StatusCodes.Status404NotFound);
        }

        var validation = Validate(offering, null, request);
        if (validation is not null)
        {
            return ServiceResult<OfferingDocumentRequirementDto>.Failure(validation.Value.Message, validation.Value.StatusCode);
        }

        if (offering.IsOpen
            && !offering.IsArchived
            && !request.IsRequired
            && !offering.DocumentRequirements.Any(item => item.IsActive && item.IsRequired))
        {
            return LastActiveRequiredConflict();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var requirement = new ProgramOfferingDocumentRequirement
        {
            PublicId = Guid.NewGuid(),
            ProgramOfferingId = offeringId,
            DocumentCode = request.DocumentCode.Trim(),
            NormalizedDocumentCode = NormalizeCode(request.DocumentCode),
            DisplayName = request.DisplayName.Trim(),
            Description = NormalizeOptional(request.Description),
            IsRequired = request.IsRequired,
            IsActive = true,
            AllowedContentCategory = request.AllowedContentCategory,
            MaximumBytes = request.MaximumBytes,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.ProgramOfferingDocumentRequirements.Add(requirement);
        TouchOffering(offering, now);
        AddAudit(adminId, "OfferingDocumentRequirementCreated", requirement.PublicId, requirement, now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ServiceResult<OfferingDocumentRequirementDto>.Success(Map(requirement), StatusCodes.Status201Created);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConfigurationConcurrencyConflict();
        }
        catch (DbUpdateException exception) when (!IsSqlServerDeadlock(exception))
        {
            return ServiceResult<OfferingDocumentRequirementDto>.Failure(
                "Bu ilan için aynı belge kodu zaten bulunuyor.",
                StatusCodes.Status409Conflict);
        }
    }

    public async Task<ServiceResult<OfferingDocumentRequirementDto>> UpdateAsync(
        int offeringId,
        Guid publicId,
        int adminId,
        OfferingDocumentRequirementUpdateDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await UpdateCoreAsync(offeringId, publicId, adminId, request, cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            && IsSqlServerDeadlock(exception))
        {
            return ConfigurationConcurrencyConflict();
        }
    }

    private async Task<ServiceResult<OfferingDocumentRequirementDto>> UpdateCoreAsync(
        int offeringId,
        Guid publicId,
        int adminId,
        OfferingDocumentRequirementUpdateDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginConfigurationTransactionIfSupportedAsync(cancellationToken);
        var offering = await LoadOfferingAggregateAsync(offeringId, cancellationToken);
        var requirement = offering?.DocumentRequirements.SingleOrDefault(item => item.PublicId == publicId);
        if (requirement is null)
        {
            return NotFound();
        }

        var validation = Validate(offering!, requirement.RequirementId, request);
        if (validation is not null)
        {
            return ServiceResult<OfferingDocumentRequirementDto>.Failure(validation.Value.Message, validation.Value.StatusCode);
        }

        if (offering!.IsOpen
            && !offering.IsArchived
            && !HasActiveRequiredAfterChange(offering, requirement, requirement.IsActive, request.IsRequired))
        {
            return LastActiveRequiredConflict();
        }

        if (!TrySetConcurrency(requirement, request.RowVersion, out var concurrencyError))
        {
            return ServiceResult<OfferingDocumentRequirementDto>.Failure(concurrencyError!, StatusCodes.Status400BadRequest);
        }

        requirement.DocumentCode = request.DocumentCode.Trim();
        requirement.NormalizedDocumentCode = NormalizeCode(request.DocumentCode);
        requirement.DisplayName = request.DisplayName.Trim();
        requirement.Description = NormalizeOptional(request.Description);
        requirement.IsRequired = request.IsRequired;
        requirement.AllowedContentCategory = request.AllowedContentCategory;
        requirement.MaximumBytes = request.MaximumBytes;
        requirement.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        TouchOffering(offering, requirement.UpdatedAtUtc);
        AddAudit(adminId, "OfferingDocumentRequirementUpdated", publicId, requirement, requirement.UpdatedAtUtc);

        return await SaveUpdateAsync(requirement, transaction, cancellationToken);
    }

    public async Task<ServiceResult<OfferingDocumentRequirementDto>> SetActiveAsync(
        int offeringId,
        Guid publicId,
        int adminId,
        DocumentRequirementActiveDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SetActiveCoreAsync(offeringId, publicId, adminId, request, cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            && IsSqlServerDeadlock(exception))
        {
            return ConfigurationConcurrencyConflict();
        }
    }

    private async Task<ServiceResult<OfferingDocumentRequirementDto>> SetActiveCoreAsync(
        int offeringId,
        Guid publicId,
        int adminId,
        DocumentRequirementActiveDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginConfigurationTransactionIfSupportedAsync(cancellationToken);
        var offering = await LoadOfferingAggregateAsync(offeringId, cancellationToken);
        var requirement = offering?.DocumentRequirements.SingleOrDefault(item => item.PublicId == publicId);
        if (requirement is null)
        {
            return NotFound();
        }

        if (offering!.IsOpen
            && !offering.IsArchived
            && !HasActiveRequiredAfterChange(offering, requirement, request.IsActive, requirement.IsRequired))
        {
            return LastActiveRequiredConflict();
        }

        if (!TrySetConcurrency(requirement, request.RowVersion, out var concurrencyError))
        {
            return ServiceResult<OfferingDocumentRequirementDto>.Failure(concurrencyError!, StatusCodes.Status400BadRequest);
        }

        requirement.IsActive = request.IsActive;
        requirement.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        TouchOffering(offering, requirement.UpdatedAtUtc);
        AddAudit(
            adminId,
            request.IsActive ? "OfferingDocumentRequirementActivated" : "OfferingDocumentRequirementDeactivated",
            publicId,
            requirement,
            requirement.UpdatedAtUtc);
        return await SaveUpdateAsync(requirement, transaction, cancellationToken);
    }

    private (string Message, int StatusCode)? Validate(
        ProgramOffering offering,
        int? currentRequirementId,
        OfferingDocumentRequirementCreateDto request)
    {
        if (request.MaximumBytes <= 0 || request.MaximumBytes > uploadOptions.Value.MaximumBytes)
        {
            return ("Belge boyutu sınırı global yükleme sınırını aşamaz.", StatusCodes.Status400BadRequest);
        }

        var normalizedCode = NormalizeCode(request.DocumentCode);
        if (normalizedCode.Length is < 2 or > 64
            || normalizedCode.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            return ("Belge kodu geçersiz.", StatusCodes.Status400BadRequest);
        }

        if (offering.DocumentRequirements.Any(
            item => item.ProgramOfferingId == offering.ProgramOfferingId
                && item.RequirementId != currentRequirementId
                && item.NormalizedDocumentCode == normalizedCode))
        {
            return ("Bu ilan için aynı belge kodu zaten bulunuyor.", StatusCodes.Status409Conflict);
        }

        return null;
    }

    private bool TrySetConcurrency(
        ProgramOfferingDocumentRequirement requirement,
        string value,
        out string? error)
    {
        try
        {
            dbContext.Entry(requirement).Property(item => item.RowVersion).OriginalValue = Convert.FromBase64String(value);
            error = null;
            return true;
        }
        catch (FormatException)
        {
            error = "Eşzamanlılık belirteci geçersiz.";
            return false;
        }
    }

    private async Task<ServiceResult<OfferingDocumentRequirementDto>> SaveUpdateAsync(
        ProgramOfferingDocumentRequirement requirement,
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

            await dbContext.Entry(requirement).ReloadAsync(cancellationToken);
            return ServiceResult<OfferingDocumentRequirementDto>.Success(Map(requirement));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConfigurationConcurrencyConflict();
        }
        catch (DbUpdateException exception) when (!IsSqlServerDeadlock(exception))
        {
            return ServiceResult<OfferingDocumentRequirementDto>.Failure(
                "Belge koşulu kaydedilemedi; belge kodu ve sınırları kontrol edin.",
                StatusCodes.Status409Conflict);
        }
    }

    private Task<ProgramOffering?> LoadOfferingAggregateAsync(
        int offeringId,
        CancellationToken cancellationToken) =>
        dbContext.ProgramOfferings
            .Include(item => item.DocumentRequirements)
            .SingleOrDefaultAsync(item => item.ProgramOfferingId == offeringId, cancellationToken);

    private void TouchOffering(ProgramOffering offering, DateTime now)
    {
        offering.UpdatedAtUtc = now;
        dbContext.Entry(offering).Property(item => item.UpdatedAtUtc).IsModified = true;
    }

    private static bool HasActiveRequiredAfterChange(
        ProgramOffering offering,
        ProgramOfferingDocumentRequirement changedRequirement,
        bool changedIsActive,
        bool changedIsRequired) =>
        offering.DocumentRequirements.Any(item =>
            item.RequirementId == changedRequirement.RequirementId
                ? changedIsActive && changedIsRequired
                : item.IsActive && item.IsRequired);

    private async Task<IDbContextTransaction?> BeginConfigurationTransactionIfSupportedAsync(
        CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private static ServiceResult<OfferingDocumentRequirementDto> LastActiveRequiredConflict() =>
        ServiceResult<OfferingDocumentRequirementDto>.Failure(
            "Açık bir ilanın son aktif zorunlu belge koşulu kaldırılamaz. Önce ilanı kapatın.",
            StatusCodes.Status409Conflict);

    private static ServiceResult<OfferingDocumentRequirementDto> ConfigurationConcurrencyConflict() =>
        ServiceResult<OfferingDocumentRequirementDto>.Failure(
            "Belge koşulu veya ilan başka bir yönetici tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
            StatusCodes.Status409Conflict);

    private static bool IsSqlServerDeadlock(Exception exception) =>
        exception is SqlException { Number: 1205 }
        || (exception.InnerException is not null && IsSqlServerDeadlock(exception.InnerException));

    private void AddAudit(
        int adminId,
        string eventType,
        Guid publicId,
        ProgramOfferingDocumentRequirement requirement,
        DateTime now) =>
        dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorAdminId = adminId,
            EventType = eventType,
            TargetType = "OfferingDocumentRequirement",
            TargetId = publicId.ToString("D"),
            Details = JsonSerializer.Serialize(new
            {
                requirement.ProgramOfferingId,
                requirement.DocumentCode,
                requirement.IsRequired,
                requirement.IsActive,
                requirement.AllowedContentCategory,
                requirement.MaximumBytes
            }),
            CreatedAtUtc = now
        });

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OfferingDocumentRequirementDto Map(ProgramOfferingDocumentRequirement requirement) => new(
        requirement.PublicId,
        requirement.DocumentCode,
        requirement.DisplayName,
        requirement.Description,
        requirement.IsRequired,
        requirement.IsActive,
        requirement.AllowedContentCategory,
        requirement.MaximumBytes,
        Convert.ToBase64String(requirement.RowVersion));

    private static ServiceResult<OfferingDocumentRequirementDto> NotFound() =>
        ServiceResult<OfferingDocumentRequirementDto>.Failure("Belge koşulu bulunamadı.", StatusCodes.Status404NotFound);
}
