using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed class ApplicationServiceTests
{
    [Fact]
    public async Task Create_builds_document_workflow_draft_and_immutable_requirement_snapshot()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        offering.DocumentRequirements.Add(new ProgramOfferingDocumentRequirement
        {
            PublicId = Guid.NewGuid(),
            DocumentCode = "transcript",
            NormalizedDocumentCode = "TRANSCRIPT",
            DisplayName = "Transkript",
            IsRequired = true,
            IsActive = true,
            AllowedContentCategory = DocumentContentCategory.PdfOnly,
            MaximumBytes = 1024,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        offering.DocumentRequirements.Single().DisplayName = "Değiştirildi";
        await db.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        var application = Assert.Single(db.Applications);
        Assert.Equal(ApplicationStatus.Draft.ToString(), application.CurrentStatus);
        Assert.True(application.UsesDocumentWorkflow);
        Assert.Equal("Transkript", Assert.Single(db.ApplicationDocumentRequirementSnapshots).DisplayName);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "DocumentDraftCreated");
    }

    [Fact]
    public async Task Activating_requirement_preserves_existing_draft_and_applies_to_new_drafts()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        AddRequirement(offering, isRequired: true);
        var requirement = offering.DocumentRequirements.Single();
        requirement.IsActive = false;
        await db.SaveChangesAsync();
        var offeringId = offering.ProgramOfferingId;
        var requirementPublicId = requirement.PublicId;
        var rowVersion = Convert.ToBase64String(requirement.RowVersion);
        db.ChangeTracker.Clear();
        var applicationService = CreateService(db);

        var existingDraft = await applicationService.CreateAsync("10000000146", offeringId, CancellationToken.None);
        var requirementService = new OfferingDocumentRequirementService(
            db,
            new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)),
            Options.Create(new DocumentUploadOptions { MaximumBytes = 2 * 1024 * 1024 }));
        var activated = await requirementService.SetActiveAsync(
            offeringId,
            requirementPublicId,
            1,
            new DocumentRequirementActiveDto { IsActive = true, RowVersion = rowVersion },
            CancellationToken.None);
        db.ChangeTracker.Clear();
        var newDraft = await applicationService.CreateAsync("10000000154", offeringId, CancellationToken.None);

        Assert.True(existingDraft.IsSuccess);
        Assert.True(activated.IsSuccess);
        Assert.True(activated.Value!.IsActive);
        Assert.True(activated.Value.IsRequired);
        Assert.True(newDraft.IsSuccess);
        var existingSnapshots = await db.ApplicationDocumentRequirementSnapshots.AsNoTracking()
            .Where(item => item.Application.PublicId == existingDraft.Value!.PublicId)
            .ToListAsync();
        var newSnapshots = await db.ApplicationDocumentRequirementSnapshots.AsNoTracking()
            .Where(item => item.Application.PublicId == newDraft.Value!.PublicId)
            .ToListAsync();
        Assert.Empty(existingSnapshots);
        var newSnapshot = Assert.Single(newSnapshots);
        Assert.True(newSnapshot.IsRequired);
        Assert.Equal("TRANSCRIPT", newSnapshot.DocumentCode);
    }

    [Fact]
    public async Task Submit_is_blocked_when_required_document_is_missing()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        AddRequirement(offering, isRequired: true);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var draft = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);

        var result = await service.SubmitAsync("10000000146", draft.Value!.PublicId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Zorunlu belgeler eksik", result.Error, StringComparison.Ordinal);
        Assert.Equal(ApplicationStatus.Draft.ToString(), db.Applications.Single().CurrentStatus);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "DocumentSubmissionBlocked");
    }

    [Fact]
    public async Task Optional_document_does_not_block_submission_and_score_is_captured_at_submit()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        AddRequirement(offering, isRequired: false);
        var exam = new Exam { ExamName = "ALES" };
        offering.ExamRequirements.Add(new ProgramOfferingExamRequirement
        {
            Exam = exam,
            MinimumScore = 70,
            IsRequired = true
        });
        db.StudentExamScores.Add(new StudentExamScore
        {
            Tc = "10000000146",
            Exam = exam,
            Score = 75,
            ExamDate = new DateOnly(2026, 1, 1)
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var draft = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        db.StudentExamScores.Single().Score = 80;
        await db.SaveChangesAsync();

        var result = await service.SubmitAsync("10000000146", draft.Value!.PublicId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationStatus.Pending.ToString(), db.Applications.Single().CurrentStatus);
        Assert.Equal(80, db.ApplicationScoreSnapshots.Single().ScoreSnapshot);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "ApplicationSubmitted");
    }

    [Fact]
    public async Task Drafts_do_not_consume_quota_or_appear_in_admin_list()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        offering.Quota = 1;
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var first = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        var second = await service.CreateAsync("10000000154", offering.ProgramOfferingId, CancellationToken.None);
        var beforeSubmit = await service.GetForAdminAsync(null, null, null, null, 1, 20, CancellationToken.None);
        var firstSubmit = await service.SubmitAsync("10000000146", first.Value!.PublicId, CancellationToken.None);
        var secondSubmit = await service.SubmitAsync("10000000154", second.Value!.PublicId, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Empty(beforeSubmit.Items);
        Assert.True(firstSubmit.IsSuccess);
        Assert.False(secondSubmit.IsSuccess);
        Assert.Contains("kontenjan", secondSubmit.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_revalidates_exam_requirement()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        offering.ExamRequirements.Add(new ProgramOfferingExamRequirement
        {
            Exam = new Exam { ExamName = "ALES" },
            MinimumScore = 70,
            IsRequired = true
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var draft = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);

        var result = await service.SubmitAsync("10000000146", draft.Value!.PublicId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("ALES", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_revalidates_offering_date_and_active_catalog_state()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        var service = CreateService(db);
        var draft = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        offering.ApplicationDeadlineUtc = new DateTime(2026, 7, 16, 23, 59, 0, DateTimeKind.Utc);
        offering.Program.Institute.IsActive = false;
        await db.SaveChangesAsync();

        var result = await service.SubmitAsync("10000000146", draft.Value!.PublicId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("açık değil", result.Error, StringComparison.Ordinal);
        Assert.Equal(ApplicationStatus.Draft.ToString(), db.Applications.Single().CurrentStatus);
    }

    [Fact]
    public async Task Student_cannot_observe_another_students_application_by_public_id()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        var draft = await CreateService(db).CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);

        var detail = await CreateService(db).GetDetailForStudentAsync(
            "10000000154",
            draft.Value!.PublicId,
            CancellationToken.None);

        Assert.Null(detail);
    }

    [Fact]
    public async Task Admin_cannot_see_or_review_draft_but_can_see_submitted_application()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        var service = CreateService(db);
        var draft = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);

        Assert.Null(await service.GetDetailForAdminAsync(draft.Value!.PublicId, CancellationToken.None));
        Assert.True((await service.SubmitAsync("10000000146", draft.Value.PublicId, CancellationToken.None)).IsSuccess);
        Assert.NotNull(await service.GetDetailForAdminAsync(draft.Value.PublicId, CancellationToken.None));
    }

    [Fact]
    public async Task Workflow_application_cannot_be_approved_until_required_current_document_is_approved()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        AddRequirement(offering, isRequired: true);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var draft = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        AddCurrentDocument(db.Applications.Single());
        await db.SaveChangesAsync();
        Assert.True((await service.SubmitAsync("10000000146", draft.Value!.PublicId, CancellationToken.None)).IsSuccess);
        Assert.True((await service.UpdateStatusAsync(
            draft.Value.PublicId,
            1,
            new ApplicationStatusUpdateDto { NewStatus = ApplicationStatus.UnderReview, RowVersion = "" },
            CancellationToken.None)).IsSuccess);

        var result = await service.UpdateStatusAsync(
            draft.Value.PublicId,
            1,
            new ApplicationStatusUpdateDto { NewStatus = ApplicationStatus.Approved, RowVersion = "" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("zorunlu", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Legacy_application_keeps_existing_status_transitions_without_document_gate()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        var legacy = new Application
        {
            PublicId = Guid.NewGuid(),
            Tc = "10000000146",
            ProgramOfferingId = offering.ProgramOfferingId,
            CurrentStatus = ApplicationStatus.Pending.ToString(),
            ApplicationDate = DateTime.UtcNow,
            UsesDocumentWorkflow = false
        };
        db.Applications.Add(legacy);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        Assert.True((await service.UpdateStatusAsync(
            legacy.PublicId,
            1,
            new ApplicationStatusUpdateDto { NewStatus = ApplicationStatus.UnderReview, RowVersion = "" },
            CancellationToken.None)).IsSuccess);
        Assert.True((await service.UpdateStatusAsync(
            legacy.PublicId,
            1,
            new ApplicationStatusUpdateDto { NewStatus = ApplicationStatus.Approved, RowVersion = "" },
            CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task Duplicate_draft_for_same_offering_is_rejected()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        var service = CreateService(db);

        Assert.True((await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None)).IsSuccess);
        var duplicate = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);

        Assert.False(duplicate.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, duplicate.StatusCode);
    }

    private static void AddRequirement(ProgramOffering offering, bool isRequired) =>
        offering.DocumentRequirements.Add(new ProgramOfferingDocumentRequirement
        {
            PublicId = Guid.NewGuid(),
            DocumentCode = isRequired ? "TRANSCRIPT" : "PORTFOLIO",
            NormalizedDocumentCode = isRequired ? "TRANSCRIPT" : "PORTFOLIO",
            DisplayName = isRequired ? "Transkript" : "Portfolyo",
            IsRequired = isRequired,
            IsActive = true,
            AllowedContentCategory = DocumentContentCategory.PdfOrImage,
            MaximumBytes = 1024 * 1024,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

    private static void AddCurrentDocument(Application application)
    {
        var requirement = application.DocumentRequirementSnapshots.Single();
        application.Documents.Add(new ApplicationDocument
        {
            PublicId = Guid.NewGuid(),
            RequirementSnapshot = requirement,
            VersionNumber = 1,
            IsCurrent = true,
            OriginalFileName = "transkript.pdf",
            ObjectKey = Guid.NewGuid().ToString("N"),
            VerifiedContentType = "application/pdf",
            FileSize = 10,
            Sha256 = new string('a', 64),
            ReviewStatus = DocumentReviewStatus.Pending,
            UploadedAtUtc = DateTime.UtcNow
        });
    }

    private static ApplicationService CreateService(GraduateAppDbContext db) =>
        new(db, new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)));

    private static async Task<ProgramOffering> SeedAsync(GraduateAppDbContext db)
    {
        var program = new GraduateApp.API.Models.Program
        {
            ProgramName = "Bilgisayar Mühendisliği",
            DegreeType = "Tezli Yüksek Lisans",
            IsActive = true,
            Institute = new Institute { InstituteName = "Fen Bilimleri", IsActive = true }
        };
        var offering = new ProgramOffering
        {
            Program = program,
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall,
            ApplicationStartUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            ApplicationDeadlineUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Quota = 10,
            IsOpen = true
        };
        db.Students.Add(CreateStudent("10000000146", "student@example.test"));
        db.Students.Add(CreateStudent("10000000154", "other@example.test"));
        db.ProgramOfferings.Add(offering);
        await db.SaveChangesAsync();
        return offering;
    }

    private static Student CreateStudent(string tc, string email) => new()
    {
        Tc = tc,
        PublicId = Guid.NewGuid(),
        StudentName = "Test",
        StudentSurname = "Öğrenci",
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        PasswordHash = "hash",
        SecurityStamp = Guid.NewGuid().ToString("N"),
        IsActive = true
    };
}
