using System.Text.Json;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;
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
        var validation = await ValidateAsync(offeringId, null, request, cancellationToken);
        if (validation is not null)
        {
            return ServiceResult<OfferingDocumentRequirementDto>.Failure(validation.Value.Message, validation.Value.StatusCode);
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
        AddAudit(adminId, "OfferingDocumentRequirementCreated", requirement.PublicId, requirement, now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult<OfferingDocumentRequirementDto>.Success(Map(requirement), StatusCodes.Status201Created);
        }
        catch (DbUpdateException)
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
        var requirement = await dbContext.ProgramOfferingDocumentRequirements.SingleOrDefaultAsync(
            item => item.ProgramOfferingId == offeringId && item.PublicId == publicId,
            cancellationToken);
        if (requirement is null)
        {
            return NotFound();
        }

        var validation = await ValidateAsync(offeringId, requirement.RequirementId, request, cancellationToken);
        if (validation is not null)
        {
            return ServiceResult<OfferingDocumentRequirementDto>.Failure(validation.Value.Message, validation.Value.StatusCode);
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
        AddAudit(adminId, "OfferingDocumentRequirementUpdated", publicId, requirement, requirement.UpdatedAtUtc);

        return await SaveUpdateAsync(requirement, cancellationToken);
    }

    public async Task<ServiceResult<OfferingDocumentRequirementDto>> SetActiveAsync(
        int offeringId,
        Guid publicId,
        int adminId,
        DocumentRequirementActiveDto request,
        CancellationToken cancellationToken)
    {
        var requirement = await dbContext.ProgramOfferingDocumentRequirements.SingleOrDefaultAsync(
            item => item.ProgramOfferingId == offeringId && item.PublicId == publicId,
            cancellationToken);
        if (requirement is null)
        {
            return NotFound();
        }

        if (!TrySetConcurrency(requirement, request.RowVersion, out var concurrencyError))
        {
            return ServiceResult<OfferingDocumentRequirementDto>.Failure(concurrencyError!, StatusCodes.Status400BadRequest);
        }

        requirement.IsActive = request.IsActive;
        requirement.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        AddAudit(
            adminId,
            request.IsActive ? "OfferingDocumentRequirementActivated" : "OfferingDocumentRequirementDeactivated",
            publicId,
            requirement,
            requirement.UpdatedAtUtc);
        return await SaveUpdateAsync(requirement, cancellationToken);
    }

    private async Task<(string Message, int StatusCode)?> ValidateAsync(
        int offeringId,
        int? currentRequirementId,
        OfferingDocumentRequirementCreateDto request,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.ProgramOfferings.AnyAsync(item => item.ProgramOfferingId == offeringId, cancellationToken))
        {
            return ("İlan bulunamadı.", StatusCodes.Status404NotFound);
        }

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

        if (await dbContext.ProgramOfferingDocumentRequirements.AnyAsync(
            item => item.ProgramOfferingId == offeringId
                && item.RequirementId != currentRequirementId
                && item.NormalizedDocumentCode == normalizedCode,
            cancellationToken))
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
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await dbContext.Entry(requirement).ReloadAsync(cancellationToken);
            return ServiceResult<OfferingDocumentRequirementDto>.Success(Map(requirement));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<OfferingDocumentRequirementDto>.Failure(
                "Belge koşulu başka bir yönetici tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException)
        {
            return ServiceResult<OfferingDocumentRequirementDto>.Failure(
                "Belge koşulu kaydedilemedi; belge kodu ve sınırları kontrol edin.",
                StatusCodes.Status409Conflict);
        }
    }

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
