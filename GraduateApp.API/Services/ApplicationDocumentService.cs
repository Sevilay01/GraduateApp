using System.Data;
using System.Data.Common;
using System.Text.Json;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Infrastructure;
using GraduateApp.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GraduateApp.API.Services;

public sealed record DocumentDownload(Stream Content, string ContentType, string FileName);

public interface IApplicationDocumentService
{
    Task<ServiceResult<ApplicationDocumentDto>> UploadAsync(
        string studentTc,
        Guid applicationPublicId,
        Guid requirementPublicId,
        IFormFile file,
        CancellationToken cancellationToken);
    Task<ServiceResult<DocumentDownload>> OpenForStudentAsync(
        string studentTc,
        Guid applicationPublicId,
        Guid documentPublicId,
        CancellationToken cancellationToken);
    Task<ServiceResult<DocumentDownload>> OpenForAdminAsync(
        Guid applicationPublicId,
        Guid documentPublicId,
        int adminId,
        CancellationToken cancellationToken);
    Task<ServiceResult<ApplicationDocumentDto>> ReviewAsync(
        Guid applicationPublicId,
        Guid documentPublicId,
        int adminId,
        DocumentReviewDto request,
        CancellationToken cancellationToken);
}

public sealed class ApplicationDocumentService(
    GraduateAppDbContext dbContext,
    IDocumentFileValidator fileValidator,
    IPrivateFileStorage storage,
    IFileMalwareScanner malwareScanner,
    TimeProvider timeProvider,
    IHostEnvironment environment) : IApplicationDocumentService
{
    public async Task<ServiceResult<ApplicationDocumentDto>> UploadAsync(
        string studentTc,
        Guid applicationPublicId,
        Guid requirementPublicId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var application = await dbContext.Applications
            .Include(item => item.DocumentRequirementSnapshots).ThenInclude(item => item.Documents)
            .SingleOrDefaultAsync(
                item => item.PublicId == applicationPublicId && item.Tc == studentTc,
                cancellationToken);
        if (application is null || !application.UsesDocumentWorkflow)
        {
            return NotFound();
        }

        var requirement = application.DocumentRequirementSnapshots.SingleOrDefault(item => item.PublicId == requirementPublicId);
        if (requirement is null)
        {
            return NotFound();
        }

        var current = requirement.Documents.SingleOrDefault(item => item.IsCurrent);
        if (!CanUpload(application.CurrentStatus, current))
        {
            return ServiceResult<ApplicationDocumentDto>.Failure(
                "Bu başvuru durumunda belge yüklenemez. Gönderilmiş başvurularda yalnızca reddedilen belge yeniden yüklenebilir.",
                StatusCodes.Status409Conflict);
        }

        if (!await ProductionProvidersReadyAsync(cancellationToken))
        {
            await AuditFailureAsync(
                application.PublicId,
                "DocumentIntegrityFailure",
                "UnsafeProviderConfiguration",
                transaction,
                cancellationToken);
            return ServiceResult<ApplicationDocumentDto>.Failure(
                "Belge güvenlik sağlayıcıları hazır olmadığı için yükleme kabul edilemiyor.",
                StatusCodes.Status503ServiceUnavailable);
        }

        var validation = await fileValidator.ValidateAsync(
            file,
            requirement.AllowedContentCategory,
            requirement.MaximumBytes,
            cancellationToken);
        if (!validation.IsSuccess || validation.Value is null)
        {
            await AuditFailureAsync(application.PublicId, "DocumentIntegrityFailure", "ValidationRejected", transaction, cancellationToken);
            return ServiceResult<ApplicationDocumentDto>.Failure(validation.Error!, validation.StatusCode);
        }

        await using var validated = validation.Value;
        try
        {
            await using var scanStream = validated.OpenRead();
            var scan = await malwareScanner.ScanAsync(scanStream, cancellationToken);
            if (!scan.CanAccept)
            {
                await AuditFailureAsync(application.PublicId, "DocumentIntegrityFailure", "ScannerRejected", transaction, cancellationToken);
                return ServiceResult<ApplicationDocumentDto>.Failure(
                    scan.Error ?? "Dosya güvenlik doğrulamasından geçemedi.",
                    StatusCodes.Status503ServiceUnavailable);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await AuditFailureAsync(application.PublicId, "DocumentIntegrityFailure", "ScannerUnavailable", transaction, cancellationToken);
            return ServiceResult<ApplicationDocumentDto>.Failure(
                "Dosya güvenlik doğrulaması tamamlanamadı.",
                StatusCodes.Status503ServiceUnavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await AuditFailureAsync(application.PublicId, "DocumentIntegrityFailure", "ScannerTimeout", transaction, cancellationToken);
            return ServiceResult<ApplicationDocumentDto>.Failure(
                "Dosya güvenlik doğrulaması tamamlanamadı.",
                StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception) when (exception is TimeoutException or HttpRequestException)
        {
            await AuditFailureAsync(application.PublicId, "DocumentIntegrityFailure", "ScannerUnavailable", transaction, cancellationToken);
            return ServiceResult<ApplicationDocumentDto>.Failure(
                "Dosya güvenlik doğrulaması tamamlanamadı.",
                StatusCodes.Status503ServiceUnavailable);
        }

        if (current is not null && string.Equals(current.Sha256, validated.Sha256, StringComparison.Ordinal))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return ServiceResult<ApplicationDocumentDto>.Failure(
                "Aynı içerik zaten bu belge koşulunun güncel sürümüdür.",
                StatusCodes.Status409Conflict);
        }

        string objectKey;
        try
        {
            await using var content = validated.OpenRead();
            objectKey = await storage.SaveAsync(content, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await AuditFailureAsync(application.PublicId, "DocumentStorageFailure", "SaveFailed", transaction, cancellationToken);
            return ServiceResult<ApplicationDocumentDto>.Failure(
                "Belge güvenli depoya kaydedilemedi. Lütfen daha sonra tekrar deneyin.",
                StatusCodes.Status503ServiceUnavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await AuditFailureAsync(application.PublicId, "DocumentStorageFailure", "SaveTimeout", transaction, cancellationToken);
            return ServiceResult<ApplicationDocumentDto>.Failure(
                "Belge güvenli depoya kaydedilemedi. Lütfen daha sonra tekrar deneyin.",
                StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception) when (exception is TimeoutException or HttpRequestException)
        {
            await AuditFailureAsync(application.PublicId, "DocumentStorageFailure", "SaveFailed", transaction, cancellationToken);
            return ServiceResult<ApplicationDocumentDto>.Failure(
                "Belge güvenli depoya kaydedilemedi. Lütfen daha sonra tekrar deneyin.",
                StatusCodes.Status503ServiceUnavailable);
        }

        if (current is not null)
        {
            current.IsCurrent = false;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var document = new ApplicationDocument
        {
            PublicId = Guid.NewGuid(),
            ApplicationId = application.ApplicationId,
            RequirementSnapshotId = requirement.SnapshotId,
            VersionNumber = requirement.Documents.Count == 0 ? 1 : requirement.Documents.Max(item => item.VersionNumber) + 1,
            IsCurrent = true,
            OriginalFileName = validated.OriginalFileName,
            ObjectKey = objectKey,
            VerifiedContentType = validated.VerifiedContentType,
            FileSize = validated.FileSize,
            Sha256 = validated.Sha256,
            ReviewStatus = DocumentReviewStatus.Pending,
            UploadedAtUtc = now
        };
        dbContext.ApplicationDocuments.Add(document);
        AddAudit(
            null,
            current is null ? "DocumentUploaded" : "DocumentReuploaded",
            document.PublicId,
            new { ApplicationPublicId = application.PublicId, RequirementPublicId = requirement.PublicId, document.VersionNumber },
            now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ServiceResult<ApplicationDocumentDto>.Success(Map(document), StatusCodes.Status201Created);
        }
        catch (OperationCanceledException)
        {
            await RollbackAndDeleteAsync(transaction, objectKey);
            throw;
        }
        catch (Exception exception) when (DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            await RollbackAndDeleteAsync(transaction, objectKey);
            throw;
        }
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            await BestEffortDeleteAsync(objectKey, cancellationToken);
            dbContext.ChangeTracker.Clear();
            AddAudit(
                null,
                "DocumentStorageFailure",
                application.PublicId,
                new { Reason = "MetadataSaveFailedCompensationAttempted" },
                timeProvider.GetUtcNow().UtcDateTime);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The request still fails closed; the storage object compensation has already been attempted.
            }

            return ServiceResult<ApplicationDocumentDto>.Failure(
                "Belge metadata kaydı tamamlanamadı; lütfen sayfayı yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
    }

    private async Task<bool> ProductionProvidersReadyAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsProduction())
        {
            return true;
        }

        return await IsProductionProviderReadyAsync(storage, cancellationToken)
            && await IsProductionProviderReadyAsync(malwareScanner, cancellationToken);
    }

    private static async ValueTask<bool> IsProductionProviderReadyAsync(
        object provider,
        CancellationToken cancellationToken)
    {
        if (provider is not IProductionReadinessProbe probe)
        {
            return false;
        }

        try
        {
            return await probe.IsReadyAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task RollbackAndDeleteAsync(
        IDbContextTransaction? transaction,
        string objectKey)
    {
        if (transaction is not null)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is InvalidOperationException or DbException)
            {
                // The request remains failed; storage compensation is still attempted below.
            }
        }

        await BestEffortDeleteAsync(objectKey, CancellationToken.None);
        dbContext.ChangeTracker.Clear();
    }

    public async Task<ServiceResult<DocumentDownload>> OpenForStudentAsync(
        string studentTc,
        Guid applicationPublicId,
        Guid documentPublicId,
        CancellationToken cancellationToken)
    {
        var document = await dbContext.ApplicationDocuments.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.PublicId == documentPublicId
                && item.Application.PublicId == applicationPublicId
                && item.Application.Tc == studentTc,
                cancellationToken);
        return document is null
            ? DownloadNotFound()
            : await OpenStoredFileAsync(document, cancellationToken);
    }

    public async Task<ServiceResult<DocumentDownload>> OpenForAdminAsync(
        Guid applicationPublicId,
        Guid documentPublicId,
        int adminId,
        CancellationToken cancellationToken)
    {
        var document = await dbContext.ApplicationDocuments.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.PublicId == documentPublicId
                && item.Application.PublicId == applicationPublicId
                && item.Application.CurrentStatus != ApplicationStatus.Draft.ToString(),
                cancellationToken);
        if (document is null)
        {
            return DownloadNotFound();
        }

        var result = await OpenStoredFileAsync(document, cancellationToken);
        if (result.IsSuccess)
        {
            AddAudit(adminId, "DocumentDownloadedByAdmin", document.PublicId, new { ApplicationPublicId = applicationPublicId }, timeProvider.GetUtcNow().UtcDateTime);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public async Task<ServiceResult<ApplicationDocumentDto>> ReviewAsync(
        Guid applicationPublicId,
        Guid documentPublicId,
        int adminId,
        DocumentReviewDto request,
        CancellationToken cancellationToken)
    {
        if (request.ReviewStatus is not (DocumentReviewStatus.Approved or DocumentReviewStatus.Rejected))
        {
            return ServiceResult<ApplicationDocumentDto>.Failure(
                "Belge yalnızca onaylanabilir veya gerekçeyle reddedilebilir.",
                StatusCodes.Status400BadRequest);
        }

        var reason = string.IsNullOrWhiteSpace(request.RejectionReason) ? null : request.RejectionReason.Trim();
        if (request.ReviewStatus == DocumentReviewStatus.Rejected && reason is null)
        {
            return ServiceResult<ApplicationDocumentDto>.Failure("Belge reddi için Türkçe gerekçe zorunludur.", StatusCodes.Status400BadRequest);
        }

        var document = await dbContext.ApplicationDocuments
            .Include(item => item.Application)
            .SingleOrDefaultAsync(item =>
                item.PublicId == documentPublicId
                && item.Application.PublicId == applicationPublicId,
                cancellationToken);
        if (document is null
            || !document.IsCurrent
            || !ApplicationStatusRules.TryParseStoredValue(document.Application.CurrentStatus, out var applicationStatus)
            || applicationStatus is not (ApplicationStatus.Pending or ApplicationStatus.UnderReview))
        {
            return ServiceResult<ApplicationDocumentDto>.Failure("Belge bulunamadı veya bu durumda incelenemez.", StatusCodes.Status404NotFound);
        }

        if (document.ReviewStatus != DocumentReviewStatus.Pending)
        {
            return ServiceResult<ApplicationDocumentDto>.Failure(
                "Yalnızca inceleme bekleyen güncel belge incelenebilir.",
                StatusCodes.Status409Conflict);
        }

        byte[] rowVersion;
        try
        {
            rowVersion = Convert.FromBase64String(request.RowVersion);
        }
        catch (FormatException)
        {
            return ServiceResult<ApplicationDocumentDto>.Failure("Eş zamanlılık belirteci geçersiz.", StatusCodes.Status400BadRequest);
        }

        dbContext.Entry(document).Property(item => item.RowVersion).OriginalValue = rowVersion;
        document.ReviewStatus = request.ReviewStatus;
        document.RejectionReason = request.ReviewStatus == DocumentReviewStatus.Rejected ? reason : null;
        document.ReviewedByAdminId = adminId;
        document.ReviewedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        AddAudit(
            adminId,
            request.ReviewStatus == DocumentReviewStatus.Approved ? "DocumentReviewApproved" : "DocumentReviewRejected",
            document.PublicId,
            new { ApplicationPublicId = applicationPublicId, document.VersionNumber },
            document.ReviewedAtUtc.Value);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await dbContext.Entry(document).ReloadAsync(cancellationToken);
            return ServiceResult<ApplicationDocumentDto>.Success(Map(document));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<ApplicationDocumentDto>.Failure(
                "Belge başka bir yönetici tarafından incelendi. Sayfayı yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
    }

    private async Task<ServiceResult<DocumentDownload>> OpenStoredFileAsync(
        ApplicationDocument document,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await storage.ExistsAsync(document.ObjectKey, cancellationToken))
            {
                AddAudit(null, "DocumentIntegrityFailure", document.PublicId, new { Reason = "ObjectMissing" }, timeProvider.GetUtcNow().UtcDateTime);
                await dbContext.SaveChangesAsync(cancellationToken);
                return DownloadNotFound();
            }

            var stream = await storage.OpenReadAsync(document.ObjectKey, cancellationToken);
            return ServiceResult<DocumentDownload>.Success(new DocumentDownload(
                stream,
                document.VerifiedContentType,
                document.OriginalFileName));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            AddAudit(null, "DocumentStorageFailure", document.PublicId, new { Reason = "OpenFailed" }, timeProvider.GetUtcNow().UtcDateTime);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult<DocumentDownload>.Failure(
                "Belge şu anda indirilemiyor.",
                StatusCodes.Status503ServiceUnavailable);
        }
    }

    private async Task AuditFailureAsync(
        Guid applicationPublicId,
        string eventType,
        string reason,
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        AddAudit(null, eventType, applicationPublicId, new { Reason = reason }, timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task BestEffortDeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(objectKey, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // The request still fails closed. Operations must investigate the storage audit trail.
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private static bool CanUpload(string storedStatus, ApplicationDocument? current) =>
        ApplicationStatusRules.TryParseStoredValue(storedStatus, out var status)
        && (status == ApplicationStatus.Draft
            || (status is ApplicationStatus.Pending or ApplicationStatus.UnderReview
                && current?.ReviewStatus == DocumentReviewStatus.Rejected));

    private void AddAudit(int? adminId, string eventType, Guid targetId, object details, DateTime now) =>
        dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorAdminId = adminId,
            EventType = eventType,
            TargetType = "ApplicationDocument",
            TargetId = targetId.ToString("D"),
            Details = JsonSerializer.Serialize(details),
            CreatedAtUtc = now
        });

    private static ApplicationDocumentDto Map(ApplicationDocument document) => new(
        document.PublicId,
        document.VersionNumber,
        document.IsCurrent,
        document.OriginalFileName,
        document.VerifiedContentType,
        document.FileSize,
        document.ReviewStatus,
        document.RejectionReason,
        DateTime.SpecifyKind(document.UploadedAtUtc, DateTimeKind.Utc),
        document.ReviewedAtUtc.HasValue ? DateTime.SpecifyKind(document.ReviewedAtUtc.Value, DateTimeKind.Utc) : null,
        Convert.ToBase64String(document.RowVersion));

    private static ServiceResult<ApplicationDocumentDto> NotFound() =>
        ServiceResult<ApplicationDocumentDto>.Failure("Başvuru veya belge koşulu bulunamadı.", StatusCodes.Status404NotFound);

    private static ServiceResult<DocumentDownload> DownloadNotFound() =>
        ServiceResult<DocumentDownload>.Failure("Belge bulunamadı.", StatusCodes.Status404NotFound);
}
