using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Infrastructure;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
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

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("PreProduction")]
    [InlineData("QA")]
    [InlineData("Test")]
    [InlineData("CustomEnvironment")]
    public async Task Development_scanner_is_fail_closed_outside_development(string environmentName)
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        var service = CreateService(
            db,
            storage,
            new DevelopmentNoOpFileMalwareScanner(),
            environmentName);

        var result = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("unsafe-provider"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(db.ApplicationDocuments);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task Development_providers_can_accept_a_document_in_development()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        var service = CreateService(
            db,
            storage,
            new DevelopmentNoOpFileMalwareScanner(),
            Environments.Development);

        var result = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("development-provider"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, storage.SaveCount);
        Assert.Single(db.ApplicationDocuments);
    }

    [Fact]
    public async Task Ready_probes_allow_document_acceptance_outside_development()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        var scanner = new ProbeScanner(ready: true);
        var service = CreateService(db, storage, scanner, Environments.Staging);

        var result = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("ready-providers"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, scanner.ScanCount);
        Assert.Equal(1, storage.SaveCount);
        Assert.Single(db.ApplicationDocuments);
    }

    [Fact]
    public async Task Unready_accepting_scanner_is_rejected_consistently_by_upload_and_health()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        var scanner = new ProbeScanner(ready: false);
        var environment = new TestHostEnvironment { EnvironmentName = Environments.Staging };
        var service = new ApplicationDocumentService(
            db,
            new DocumentFileValidator(Options.Create(new DocumentUploadOptions { MaximumBytes = 1024 * 1024 })),
            storage,
            scanner,
            new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)),
            environment);
        var healthCheck = new ProviderReadinessHealthCheck(
            storage,
            scanner,
            new ReadyDataProtectionProbe(),
            environment);

        var upload = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("unready-accepting-scanner"),
            CancellationToken.None);
        var health = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.False(upload.IsSuccess);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, upload.StatusCode);
        Assert.Equal(HealthStatus.Unhealthy, health.Status);
        Assert.Equal(0, scanner.ScanCount);
        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(db.ApplicationDocuments);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task Readiness_timeout_is_fail_closed_without_scan_storage_metadata_or_audit()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        var scanner = new ThrowingReadinessScanner(new TimeoutException("simulated readiness timeout"));
        var service = CreateService(db, storage, scanner, "PreProduction");

        var result = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("readiness-timeout"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal(0, scanner.ScanCount);
        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(db.ApplicationDocuments);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task Caller_cancellation_during_readiness_is_propagated_without_side_effects()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        using var cancellation = new CancellationTokenSource();
        var scanner = new CallerCancelingReadinessScanner(cancellation);
        var service = CreateService(db, storage, scanner, Environments.Staging);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("readiness-cancelled"),
            cancellation.Token));

        Assert.Equal(0, scanner.ScanCount);
        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(db.ApplicationDocuments);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task Scanner_timeout_is_fail_closed_without_storage_write()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        var service = CreateService(db, storage, new TimeoutScanner());

        var result = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("scanner-timeout"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(db.ApplicationDocuments);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "DocumentIntegrityFailure");
    }

    [Fact]
    public async Task Ambiguous_scanner_result_is_fail_closed()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        var service = CreateService(db, storage, new AmbiguousScanner());

        var result = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("ambiguous"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(db.ApplicationDocuments);
    }

    [Fact]
    public async Task Caller_cancellation_during_scan_creates_no_audit_document_or_storage_object()
    {
        await using var db = TestDb.Create();
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(db, storage, new CallerCancelingScanner(cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("cancelled"),
            cancellation.Token));

        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(db.ApplicationDocuments);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task Cancellation_during_metadata_save_compensates_storage_and_success_audit()
    {
        await using var db = TestDb.Create(new CancelDocumentMetadataSaveInterceptor());
        var application = await SeedAsync(db);
        var storage = new FakeStorage();
        var service = CreateService(db, storage);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("cancelled-save"),
            CancellationToken.None));

        Assert.Single(storage.DeletedKeys);
        Assert.Empty(db.ApplicationDocuments);
        Assert.Empty(db.SecurityAuditLogs);
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
    public async Task Approved_document_can_be_replaced_before_deadline_and_resets_eligibility()
    {
        await using var db = TestDb.Create();
        var storage = new FakeStorage();
        var service = CreateService(db, storage);
        var (application, approvedVersion) = await SeedPendingDocumentAsync(
            db,
            service,
            ApplicationStatus.UnderReview);
        approvedVersion.ReviewStatus = DocumentReviewStatus.Approved;
        approvedVersion.ReviewedByAdminId = 7;
        approvedVersion.ReviewedAtUtc = ReviewTimeUtc.AddMinutes(-5);
        application.UsesEvaluationWorkflow = true;
        application.ProgramOffering.UsesEvaluationWorkflow = true;
        application.Evaluation = new ApplicationEvaluation
        {
            ProgramOfferingId = application.ProgramOfferingId,
            EligibilityStatus = EvaluationEligibilityStatus.Eligible,
            TotalScore = 82.5m,
            EligibilityDecidedByAdminId = 7,
            EligibilityDecidedAtUtc = ReviewTimeUtc.AddMinutes(-4)
        };
        await db.SaveChangesAsync();

        var result = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("replacement-before-deadline"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var current = db.ApplicationDocuments.Single(item => item.IsCurrent);
        Assert.Equal(2, current.VersionNumber);
        Assert.Equal(DocumentReviewStatus.Pending, current.ReviewStatus);
        Assert.False(approvedVersion.IsCurrent);
        Assert.Equal(DocumentReviewStatus.Approved, approvedVersion.ReviewStatus);
        Assert.Equal(EvaluationEligibilityStatus.Pending, application.Evaluation.EligibilityStatus);
        Assert.Null(application.Evaluation.TotalScore);
        Assert.Null(application.Evaluation.EligibilityDecidedByAdminId);
        Assert.Null(application.Evaluation.EligibilityDecidedAtUtc);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "DocumentReuploaded");
    }

    [Fact]
    public async Task Document_replacement_after_deadline_is_rejected_without_side_effects()
    {
        await using var db = TestDb.Create();
        var storage = new FakeStorage();
        var service = CreateService(db, storage);
        var (application, current) = await SeedPendingDocumentAsync(
            db,
            service,
            ApplicationStatus.Pending);
        application.ProgramOffering.ApplicationDeadlineUtc = ReviewTimeUtc.AddSeconds(-1);
        await db.SaveChangesAsync();
        var saveCount = storage.SaveCount;
        var auditCount = await db.SecurityAuditLogs.CountAsync();

        var result = await service.UploadAsync(
            application.Tc,
            application.PublicId,
            application.DocumentRequirementSnapshots.Single().PublicId,
            Pdf("replacement-after-deadline"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(saveCount, storage.SaveCount);
        Assert.Equal(auditCount, await db.SecurityAuditLogs.CountAsync());
        Assert.True(current.IsCurrent);
        Assert.Single(db.ApplicationDocuments);
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

    private static ApplicationDocumentService CreateService(
        GraduateAppDbContext db,
        FakeStorage storage,
        IFileMalwareScanner? scanner = null,
        string environmentName = "Development") => new(
        db,
        new DocumentFileValidator(Options.Create(new DocumentUploadOptions { MaximumBytes = 1024 * 1024 })),
        storage,
        scanner ?? new DevelopmentNoOpFileMalwareScanner(),
        new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)),
        new TestHostEnvironment { EnvironmentName = environmentName });

    private static async Task<Application> SeedAsync(GraduateAppDbContext db)
    {
        var institute = new Institute
        {
            InstituteId = 1,
            InstituteName = "Test Enstitüsü",
            IsActive = true
        };
        var program = new GraduateApp.API.Models.Program
        {
            ProgramId = 1,
            InstituteId = institute.InstituteId,
            ProgramName = "Test Programı",
            DegreeType = "Tezli Yüksek Lisans",
            IsActive = true,
            Institute = institute
        };
        var offering = new ProgramOffering
        {
            ProgramOfferingId = 1,
            ProgramId = program.ProgramId,
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall,
            ApplicationStartUtc = ReviewTimeUtc.AddDays(-1),
            ApplicationDeadlineUtc = ReviewTimeUtc.AddDays(1),
            Quota = 10,
            IsOpen = true,
            IsArchived = false,
            EvaluationState = OfferingEvaluationState.Configuring,
            Program = program
        };
        var application = new Application
        {
            PublicId = Guid.NewGuid(),
            Tc = "10000000146",
            ProgramOfferingId = offering.ProgramOfferingId,
            ProgramOffering = offering,
            ApplicationDate = ReviewTimeUtc,
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

    private sealed class FakeStorage : IPrivateFileStorage, IProductionReadinessProbe
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

        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }
    }

    private sealed class TimeoutScanner : IFileMalwareScanner
    {
        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken) =>
            Task.FromException<MalwareScanResult>(new TaskCanceledException("simulated scanner timeout"));
    }

    private sealed class AmbiguousScanner : IFileMalwareScanner
    {
        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken) =>
            Task.FromResult(new MalwareScanResult(false, null));
    }

    private sealed class ProbeScanner(bool ready) : IFileMalwareScanner, IProductionReadinessProbe
    {
        public int ScanCount { get; private set; }

        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ready);
        }

        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
        {
            ScanCount++;
            return Task.FromResult(new MalwareScanResult(true, null));
        }
    }

    private sealed class ThrowingReadinessScanner(Exception exception)
        : IFileMalwareScanner, IProductionReadinessProbe
    {
        public int ScanCount { get; private set; }

        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<bool>(exception);

        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
        {
            ScanCount++;
            return Task.FromResult(new MalwareScanResult(true, null));
        }
    }

    private sealed class CallerCancelingReadinessScanner(CancellationTokenSource cancellation)
        : IFileMalwareScanner, IProductionReadinessProbe
    {
        public int ScanCount { get; private set; }

        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
        {
            ScanCount++;
            return Task.FromResult(new MalwareScanResult(true, null));
        }
    }

    private sealed class ReadyDataProtectionProbe : IDataProtectionReadinessProbe
    {
        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }
    }

    private sealed class CallerCancelingScanner(CancellationTokenSource cancellation) : IFileMalwareScanner
    {
        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation was expected.");
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

    private sealed class CancelDocumentMetadataSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ApplicationDocument>()
                .Any(item => item.State == EntityState.Added))
            {
                return ValueTask.FromException<InterceptionResult<int>>(
                    new OperationCanceledException("simulated metadata cancellation"));
            }

            return ValueTask.FromResult(result);
        }
    }

}
