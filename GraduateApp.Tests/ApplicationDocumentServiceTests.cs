using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed class ApplicationDocumentServiceTests
{
    [Fact]
    public async Task Storage_failure_leaves_no_document_metadata()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage { FailSave = true };
        var service = CreateService(db, storage);

        var result = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("one"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(db.ApplicationDocuments);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "DocumentStorageFailure");
    }

    [Fact]
    public async Task Database_failure_removes_new_storage_object()
    {
        var interceptor = new FailDocumentMetadataSaveInterceptor();
        await using var db = TestDb.Create(interceptor);
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        var service = CreateService(db, storage);

        var result = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("one"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Single(storage.DeletedKeys);
        Assert.Empty(db.ApplicationDocuments);
    }

    [Fact]
    public async Task Reupload_preserves_history_and_leaves_exactly_one_current_version()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        var service = CreateService(db, storage);
        var requirementId = application.DocumentRequirementSnapshots.Single().PublicId;

        var first = await service.UploadAsync(application.Tc, application.PublicId, requirementId, Pdf("one"), CancellationToken.None);
        var second = await service.UploadAsync(application.Tc, application.PublicId, requirementId, Pdf("two"), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, db.ApplicationDocuments.Count());
        Assert.Single(db.ApplicationDocuments.Where(item => item.IsCurrent));
        Assert.Equal(2, db.ApplicationDocuments.Single(item => item.IsCurrent).VersionNumber);
        Assert.Contains(db.ApplicationDocuments, item => !item.IsCurrent && item.VersionNumber == 1);
    }

    [Fact]
    public async Task Rejected_document_reupload_becomes_pending_and_keeps_rejection_in_history()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var service = CreateService(db, new FakeStorage());
        var requirementId = application.DocumentRequirementSnapshots.Single().PublicId;
        await service.UploadAsync(application.Tc, application.PublicId, requirementId, Pdf("one"), CancellationToken.None);
        var rejected = db.ApplicationDocuments.Single();
        rejected.ReviewStatus = DocumentReviewStatus.Rejected;
        rejected.RejectionReason = "Belge okunamıyor.";
        application.CurrentStatus = ApplicationStatus.Pending.ToString();
        await db.SaveChangesAsync();

        var result = await service.UploadAsync(application.Tc, application.PublicId, requirementId, Pdf("two"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentReviewStatus.Pending, db.ApplicationDocuments.Single(item => item.IsCurrent).ReviewStatus);
        Assert.Equal(DocumentReviewStatus.Rejected, db.ApplicationDocuments.Single(item => !item.IsCurrent).ReviewStatus);
        Assert.Equal("Belge okunamıyor.", db.ApplicationDocuments.Single(item => !item.IsCurrent).RejectionReason);
    }

    [Fact]
    public async Task Pending_document_can_be_approved()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeStorage());
        var (application, document) = await SeedPendingDocumentAsync(db, service, ApplicationStatus.Pending);

        var result = await service.ReviewAsync(
            application.PublicId,
            document.PublicId,
            17,
            Review(DocumentReviewStatus.Approved, document),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentReviewStatus.Approved, document.ReviewStatus);
        Assert.Null(document.RejectionReason);
        Assert.Equal(17, document.ReviewedByAdminId);
        Assert.Equal(ReviewTimeUtc, document.ReviewedAtUtc);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "DocumentReviewApproved");
    }

    [Fact]
    public async Task Pending_document_can_be_rejected()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeStorage());
        var (application, document) = await SeedPendingDocumentAsync(db, service, ApplicationStatus.Pending);

        var result = await service.ReviewAsync(
            application.PublicId,
            document.PublicId,
            17,
            Review(DocumentReviewStatus.Rejected, document, "Belge okunamıyor."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentReviewStatus.Rejected, document.ReviewStatus);
        Assert.Equal("Belge okunamıyor.", document.RejectionReason);
        Assert.Equal(17, document.ReviewedByAdminId);
        Assert.Equal(ReviewTimeUtc, document.ReviewedAtUtc);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "DocumentReviewRejected");
    }

    [Fact]
    public async Task Rejected_current_version_cannot_be_approved_again()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeStorage());
        var (application, document) = await SeedPendingDocumentAsync(db, service, ApplicationStatus.Pending);
        document.ReviewStatus = DocumentReviewStatus.Rejected;
        document.RejectionReason = "Belge okunamıyor.";
        document.ReviewedByAdminId = 7;
        document.ReviewedAtUtc = ReviewTimeUtc.AddMinutes(-5);
        await db.SaveChangesAsync();
        var auditCount = await db.SecurityAuditLogs.CountAsync();

        var result = await service.ReviewAsync(
            application.PublicId,
            document.PublicId,
            19,
            Review(DocumentReviewStatus.Approved, document),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal("Yalnızca inceleme bekleyen güncel belge incelenebilir.", result.Error);
        Assert.Equal(DocumentReviewStatus.Rejected, document.ReviewStatus);
        Assert.Equal("Belge okunamıyor.", document.RejectionReason);
        Assert.Equal(7, document.ReviewedByAdminId);
        Assert.Equal(ReviewTimeUtc.AddMinutes(-5), document.ReviewedAtUtc);
        Assert.Equal(auditCount, await db.SecurityAuditLogs.CountAsync());
    }

    [Fact]
    public async Task Approved_current_version_cannot_be_rejected_again()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeStorage());
        var (application, document) = await SeedPendingDocumentAsync(db, service, ApplicationStatus.UnderReview);
        document.ReviewStatus = DocumentReviewStatus.Approved;
        document.ReviewedByAdminId = 7;
        document.ReviewedAtUtc = ReviewTimeUtc.AddMinutes(-5);
        await db.SaveChangesAsync();
        var auditCount = await db.SecurityAuditLogs.CountAsync();

        var result = await service.ReviewAsync(
            application.PublicId,
            document.PublicId,
            19,
            Review(DocumentReviewStatus.Rejected, document, "Yeni ret gerekçesi."),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal("Yalnızca inceleme bekleyen güncel belge incelenebilir.", result.Error);
        Assert.Equal(DocumentReviewStatus.Approved, document.ReviewStatus);
        Assert.Null(document.RejectionReason);
        Assert.Equal(7, document.ReviewedByAdminId);
        Assert.Equal(ReviewTimeUtc.AddMinutes(-5), document.ReviewedAtUtc);
        Assert.Equal(auditCount, await db.SecurityAuditLogs.CountAsync());
    }

    [Fact]
    public async Task New_pending_version_of_rejected_document_can_be_reviewed()
    {
        await using var db = TestDb.Create();
        var storage = new FakeStorage();
        var service = CreateService(db, storage);
        var (application, rejectedVersion) = await SeedPendingDocumentAsync(db, service, ApplicationStatus.Pending);
        var requirementId = application.DocumentRequirementSnapshots.Single().PublicId;
        var rejected = await service.ReviewAsync(
            application.PublicId,
            rejectedVersion.PublicId,
            17,
            Review(DocumentReviewStatus.Rejected, rejectedVersion, "Belge okunamıyor."),
            CancellationToken.None);

        var uploaded = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            requirementId,
            Pdf("replacement"),
            CancellationToken.None);

        Assert.True(rejected.IsSuccess);
        Assert.True(uploaded.IsSuccess);
        Assert.Equal(DocumentReviewStatus.Pending, uploaded.Value!.ReviewStatus);
        var newVersion = db.ApplicationDocuments.Single(item => item.IsCurrent);

        var reviewed = await service.ReviewAsync(
            application.PublicId,
            newVersion.PublicId,
            19,
            Review(DocumentReviewStatus.Approved, newVersion),
            CancellationToken.None);

        Assert.True(reviewed.IsSuccess);
        Assert.Equal(DocumentReviewStatus.Approved, newVersion.ReviewStatus);
        Assert.Equal(DocumentReviewStatus.Rejected, rejectedVersion.ReviewStatus);
        Assert.False(rejectedVersion.IsCurrent);
        Assert.Equal(2, newVersion.VersionNumber);
    }

    [Fact]
    public async Task Failed_repeat_review_does_not_create_audit_or_mutate_document()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeStorage());
        var (application, document) = await SeedPendingDocumentAsync(db, service, ApplicationStatus.UnderReview);
        var first = await service.ReviewAsync(
            application.PublicId,
            document.PublicId,
            17,
            Review(DocumentReviewStatus.Rejected, document, "Eksik sayfa var."),
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        var reviewStatus = document.ReviewStatus;
        var rejectionReason = document.RejectionReason;
        var reviewedBy = document.ReviewedByAdminId;
        var reviewedAt = document.ReviewedAtUtc;
        var auditCount = await db.SecurityAuditLogs.CountAsync();

        var repeated = await service.ReviewAsync(
            application.PublicId,
            document.PublicId,
            19,
            Review(DocumentReviewStatus.Approved, document),
            CancellationToken.None);

        Assert.False(repeated.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, repeated.StatusCode);
        Assert.Equal(reviewStatus, document.ReviewStatus);
        Assert.Equal(rejectionReason, document.RejectionReason);
        Assert.Equal(reviewedBy, document.ReviewedByAdminId);
        Assert.Equal(reviewedAt, document.ReviewedAtUtc);
        Assert.Equal(auditCount, await db.SecurityAuditLogs.CountAsync());
    }

    [Fact]
    public async Task Stale_document_rowversion_returns_existing_safe_concurrency_conflict()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeStorage());
        var (application, document) = await SeedPendingDocumentAsync(db, service, ApplicationStatus.Pending);

        var result = await service.ReviewAsync(
            application.PublicId,
            document.PublicId,
            17,
            new DocumentReviewDto
            {
                ReviewStatus = DocumentReviewStatus.Approved,
                RowVersion = Convert.ToBase64String([1])
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(
            "Belge başka bir yönetici tarafından incelendi. Sayfayı yenileyip tekrar deneyin.",
            result.Error);
        var persisted = await db.ApplicationDocuments.AsNoTracking().SingleAsync();
        Assert.Equal(DocumentReviewStatus.Pending, persisted.ReviewStatus);
        Assert.Null(persisted.ReviewedByAdminId);
        Assert.Null(persisted.ReviewedAtUtc);
    }

    [Theory]
    [InlineData(ApplicationStatus.Pending)]
    [InlineData(ApplicationStatus.UnderReview)]
    public async Task Pending_document_can_be_reviewed_in_active_application_statuses(ApplicationStatus status)
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeStorage());
        var (application, document) = await SeedPendingDocumentAsync(db, service, status);

        var result = await service.ReviewAsync(
            application.PublicId,
            document.PublicId,
            17,
            Review(DocumentReviewStatus.Approved, document),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentReviewStatus.Approved, document.ReviewStatus);
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft)]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public async Task Documents_cannot_be_reviewed_in_inactive_application_statuses(ApplicationStatus status)
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeStorage());
        var (application, document) = await SeedPendingDocumentAsync(db, service, status);
        var auditCount = await db.SecurityAuditLogs.CountAsync();

        var result = await service.ReviewAsync(
            application.PublicId,
            document.PublicId,
            17,
            Review(DocumentReviewStatus.Approved, document),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal(DocumentReviewStatus.Pending, document.ReviewStatus);
        Assert.Null(document.ReviewedByAdminId);
        Assert.Null(document.ReviewedAtUtc);
        Assert.Equal(auditCount, await db.SecurityAuditLogs.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_non_current_document_keeps_safe_not_found(bool useMissingId)
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeStorage());
        var (application, document) = await SeedPendingDocumentAsync(db, service, ApplicationStatus.Pending);
        if (!useMissingId)
        {
            document.IsCurrent = false;
            await db.SaveChangesAsync();
        }

        var result = await service.ReviewAsync(
            application.PublicId,
            useMissingId ? Guid.NewGuid() : document.PublicId,
            17,
            Review(DocumentReviewStatus.Approved, document),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal(DocumentReviewStatus.Pending, document.ReviewStatus);
        Assert.Null(document.ReviewedByAdminId);
        Assert.Null(document.ReviewedAtUtc);
    }

    [Fact]
    public async Task Another_student_receives_not_found_and_storage_is_not_touched()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        var service = CreateService(db, storage);

        var result = await service.UploadAsync(
            "10000000154",
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("one"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal(0, storage.SaveCount);
    }

    [Fact]
    public async Task Another_student_cannot_download_existing_document()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        var service = CreateService(db, storage);
        var uploaded = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("one"),
            CancellationToken.None);

        var result = await service.OpenForStudentAsync(
            "10000000154",
            application.PublicId,
            uploaded.Value!.PublicId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
    }

    [Fact]
    public async Task Identical_current_content_is_explicitly_rejected()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        var service = CreateService(db, storage);
        var requirementId = application.DocumentRequirementSnapshots.Single().PublicId;
        await service.UploadAsync(application.Tc, application.PublicId, requirementId, Pdf("same"), CancellationToken.None);

        var duplicate = await service.UploadAsync(application.Tc, application.PublicId, requirementId, Pdf("same"), CancellationToken.None);

        Assert.False(duplicate.IsSuccess);
        Assert.Contains("Aynı içerik", duplicate.Error, StringComparison.Ordinal);
        Assert.Single(db.ApplicationDocuments);
        Assert.Equal(1, storage.SaveCount);
    }

    [Fact]
    public void Public_document_dto_does_not_expose_storage_key_hash_or_internal_ids()
    {
        var properties = typeof(GraduateApp.API.DTOs.ApplicationDocumentDto)
            .GetProperties()
            .Select(item => item.Name)
            .ToArray();

        Assert.DoesNotContain("ObjectKey", properties);
        Assert.DoesNotContain("Sha256", properties);
        Assert.DoesNotContain("DocumentId", properties);
        Assert.DoesNotContain("ApplicationId", properties);
        Assert.DoesNotContain("RequirementSnapshotId", properties);
    }

    private static readonly DateTime ReviewTimeUtc = new(2026, 7, 17, 9, 0, 0, DateTimeKind.Utc);

    private static DocumentReviewDto Review(
        DocumentReviewStatus status,
        ApplicationDocument document,
        string? rejectionReason = null) => new()
        {
            ReviewStatus = status,
            RejectionReason = rejectionReason,
            RowVersion = Convert.ToBase64String(document.RowVersion)
        };

    private static async Task<(Application Application, ApplicationDocument Document)> SeedPendingDocumentAsync(
        GraduateAppDbContext db,
        ApplicationDocumentService service,
        ApplicationStatus applicationStatus)
    {
        var application = await SeedAsync(db);
        var upload = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("review"),
            CancellationToken.None);
        Assert.True(upload.IsSuccess);
        application.CurrentStatus = applicationStatus.ToString();
        await db.SaveChangesAsync();
        return (application, db.ApplicationDocuments.Single());
    }

    private static ApplicationDocumentService CreateService(GraduateAppDbContext db, FakeStorage storage) => new(
        db,
        new DocumentFileValidator(Options.Create(new DocumentUploadOptions { MaximumBytes = 1024 * 1024 })),
        storage,
        new DevelopmentNoOpFileMalwareScanner(),
        new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)));

    private static async Task<Application> SeedAsync(GraduateAppDbContext db)
    {
        var application = new Application
        {
            PublicId = Guid.NewGuid(),
            Tc = "10000000146",
            ProgramOfferingId = 1,
            ApplicationDate = DateTime.UtcNow,
            CurrentStatus = ApplicationStatus.Draft.ToString(),
            UsesDocumentWorkflow = true
        };
        application.DocumentRequirementSnapshots.Add(new ApplicationDocumentRequirementSnapshot
        {
            PublicId = Guid.NewGuid(),
            DocumentCode = "TRANSCRIPT",
            DisplayName = "Transkript",
            IsRequired = true,
            AllowedContentCategory = DocumentContentCategory.PdfOnly,
            MaximumBytes = 1024 * 1024
        });
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        return application;
    }

    private static FormFile Pdf(string suffix)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes($"%PDF-1.7\n{suffix}");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "document.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    private sealed class FakeStorage : IPrivateFileStorage
    {
        private readonly Dictionary<string, byte[]> files = [];
        public bool FailSave { get; init; }
        public int SaveCount { get; private set; }
        public List<string> DeletedKeys { get; } = [];

        public async Task<string> SaveAsync(Stream source, CancellationToken cancellationToken)
        {
            SaveCount++;
            if (FailSave)
            {
                throw new IOException("simulated");
            }

            var key = Guid.NewGuid().ToString("N");
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            files[key] = buffer.ToArray();
            return key;
        }

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(files[objectKey], writable: false));

        public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.FromResult(files.ContainsKey(objectKey));

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
        {
            DeletedKeys.Add(objectKey);
            files.Remove(objectKey);
            return Task.CompletedTask;
        }
    }

    private sealed class FailDocumentMetadataSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ApplicationDocument>()
                .Any(item => item.State == EntityState.Added))
            {
                return ValueTask.FromException<InterceptionResult<int>>(new DbUpdateException("simulated"));
            }

            return ValueTask.FromResult(result);
        }
    }

}
