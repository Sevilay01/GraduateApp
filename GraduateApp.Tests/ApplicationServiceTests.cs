using System.Text.Json;
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
    public async Task Evaluation_workflow_submit_freezes_gpa_exam_and_manual_component_policy()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        await ConfigureEvaluationAsync(db, offering, "10000000146");
        var service = CreateService(db);
        var draft = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        Assert.True(db.Applications.Single().UsesEvaluationWorkflow);
        AddCurrentDocument(db.Applications.Single());
        await db.SaveChangesAsync();

        var result = await service.SubmitAsync("10000000146", draft.Value!.PublicId, CancellationToken.None);
        db.EducationInfos.Single().Gno = 1m;
        db.StudentExamScores.Single().Score = 20m;
        await db.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        var evaluation = Assert.Single(db.ApplicationEvaluations);
        Assert.Equal(EvaluationEligibilityStatus.Pending, evaluation.EligibilityStatus);
        Assert.Null(evaluation.TotalScore);
        Assert.Equal(3, evaluation.Components.Count);
        var gpa = evaluation.Components.Single(item => item.SourceTypeSnapshot == EvaluationCriterionSourceType.UndergraduateGpa);
        var exam = evaluation.Components.Single(item => item.SourceTypeSnapshot == EvaluationCriterionSourceType.ExamScore);
        var manual = evaluation.Components.Single(item => item.SourceTypeSnapshot == EvaluationCriterionSourceType.ManualScore);
        Assert.Equal(3.25m, gpa.RawScore);
        Assert.Equal(80m, exam.RawScore);
        Assert.Null(manual.RawScore);
        Assert.Null(manual.WeightedScore);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invalid_automatic_evaluation_input_leaves_draft_without_partial_snapshot_or_submitted_audit(
        bool removeGpa)
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        await ConfigureEvaluationAsync(db, offering, "10000000146");
        if (removeGpa)
        {
            db.EducationInfos.Single().Gno = null;
        }
        else
        {
            offering.EvaluationCriteria.Single(item =>
                item.SourceType == EvaluationCriterionSourceType.ExamScore).MaximumRawScore = 50m;
        }

        await db.SaveChangesAsync();
        var service = CreateService(db);
        var draft = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        var application = db.Applications.Single();
        AddCurrentDocument(application);
        await db.SaveChangesAsync();

        var result = await service.SubmitAsync("10000000146", draft.Value!.PublicId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(ApplicationStatus.Draft.ToString(), application.CurrentStatus);
        Assert.Empty(db.ApplicationEvaluations);
        Assert.Empty(db.ApplicationEvaluationComponents);
        Assert.Empty(db.ApplicationScoreSnapshots);
        Assert.DoesNotContain(db.SecurityAuditLogs, item => item.EventType == "ApplicationSubmitted");
        Assert.Contains(db.SecurityAuditLogs, item =>
            item.EventType == "DocumentSubmissionBlocked" && item.Details!.Contains("EvaluationPolicy"));
    }

    [Fact]
    public async Task Evaluation_workflow_allows_second_student_to_create_and_submit_after_quota_is_reached()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        offering.Quota = 1;
        await ConfigureEvaluationAsync(db, offering, "10000000154");
        offering.Applications.Add(new Application
        {
            PublicId = Guid.NewGuid(),
            Tc = "10000000146",
            ApplicationDate = DateTime.UtcNow,
            CurrentStatus = ApplicationStatus.Pending.ToString(),
            UsesDocumentWorkflow = true,
            UsesEvaluationWorkflow = true
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var draft = await service.CreateAsync("10000000154", offering.ProgramOfferingId, CancellationToken.None);
        AddCurrentDocument(db.Applications.Single(item => item.PublicId == draft.Value!.PublicId));
        await db.SaveChangesAsync();

        var result = await service.SubmitAsync("10000000154", draft.Value!.PublicId, CancellationToken.None);

        Assert.True(draft.IsSuccess);
        Assert.True(db.Applications.Single(item => item.PublicId == draft.Value.PublicId).UsesEvaluationWorkflow);
        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationStatus.Pending.ToString(), db.Applications.Single(item => item.PublicId == draft.Value.PublicId).CurrentStatus);
        Assert.Equal(2, db.Applications.Count(item => item.CurrentStatus == ApplicationStatus.Pending.ToString()));
    }

    [Fact]
    public async Task Evaluation_workflow_application_cannot_be_manually_approved_or_rejected()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        var application = AddWorkflowApplication(db, offering, "10000000146", ApplicationStatus.UnderReview, true);
        application.UsesEvaluationWorkflow = true;
        await db.SaveChangesAsync();
        var service = CreateService(db);

        foreach (var next in new[] { ApplicationStatus.Approved, ApplicationStatus.Rejected })
        {
            var result = await service.UpdateStatusAsync(
                application.PublicId,
                7,
                new ApplicationStatusUpdateDto
                {
                    NewStatus = next,
                    RowVersion = Convert.ToBase64String(application.RowVersion)
                },
                CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        }

        Assert.Equal(ApplicationStatus.UnderReview.ToString(), application.CurrentStatus);
        Assert.DoesNotContain(db.SecurityAuditLogs, item => item.EventType == "ApplicationStatusChanged");
    }

    [Fact]
    public async Task Create_builds_document_workflow_draft_and_immutable_requirement_snapshot()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
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
        AddRequirement(offering, isRequired: true, code: "DIPLOMA");
        var requirement = offering.DocumentRequirements.Single(item => item.NormalizedDocumentCode == "DIPLOMA");
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
        Assert.Equal(["TRANSCRIPT"], existingSnapshots.Select(item => item.DocumentCode));
        Assert.Equal(["DIPLOMA", "TRANSCRIPT"], newSnapshots.Select(item => item.DocumentCode).OrderBy(item => item));
        Assert.All(newSnapshots, item => Assert.True(item.IsRequired));
    }

    [Fact]
    public async Task Create_rejects_offering_without_active_requirements_without_side_effects()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        offering.DocumentRequirements.Single().IsActive = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Empty(db.Applications);
        Assert.Empty(db.ApplicationStatusHistories);
        Assert.DoesNotContain(db.SecurityAuditLogs, item => item.EventType == "DocumentDraftCreated");
    }

    [Fact]
    public async Task Create_rejects_offering_with_only_optional_active_requirements_without_side_effects()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        offering.DocumentRequirements.Single().IsRequired = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Empty(db.Applications);
        Assert.DoesNotContain(db.SecurityAuditLogs, item => item.EventType == "DocumentDraftCreated");
    }

    [Fact]
    public async Task Create_rejects_orphaned_open_evaluation_policy_before_draft_snapshot_or_audit()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        await ConfigureEvaluationAsync(db, offering, "10000000146");
        offering.ExamRequirements.Single().IsRequired = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).CreateAsync(
            "10000000146",
            offering.ProgramOfferingId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Contains("ALES", result.Error, StringComparison.Ordinal);
        Assert.Empty(db.Applications);
        Assert.Empty(db.ApplicationStatusHistories);
        Assert.Empty(db.ApplicationDocumentRequirementSnapshots);
        Assert.Empty(db.ApplicationScoreSnapshots);
        Assert.Empty(db.ApplicationEvaluations);
        Assert.Empty(db.ApplicationEvaluationComponents);
        Assert.DoesNotContain(db.SecurityAuditLogs, item => item.EventType == "DocumentDraftCreated");
    }

    [Fact]
    public async Task Submit_is_blocked_when_required_document_is_missing()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        var service = CreateService(db);
        var draft = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);

        var result = await service.SubmitAsync("10000000146", draft.Value!.PublicId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Contains("Zorunlu belgeler eksik", result.Error, StringComparison.Ordinal);
        AssertSubmissionBlocked(db, db.Applications.Single(), "MissingRequiredDocuments");
    }

    [Fact]
    public async Task Submit_is_blocked_when_workflow_draft_has_no_requirement_snapshots()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        var application = AddWorkflowApplication(db, offering, "10000000146", ApplicationStatus.Draft, null);
        await db.SaveChangesAsync();

        var result = await CreateService(db).SubmitAsync(
            application.Tc,
            application.PublicId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        AssertSubmissionBlocked(db, application, "NoRequirementSnapshots");
    }

    [Fact]
    public async Task Submit_is_blocked_when_workflow_draft_has_only_optional_snapshots()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        var application = AddWorkflowApplication(db, offering, "10000000146", ApplicationStatus.Draft, false);
        await db.SaveChangesAsync();

        var result = await CreateService(db).SubmitAsync(
            application.Tc,
            application.PublicId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        AssertSubmissionBlocked(db, application, "NoRequiredRequirementSnapshots");
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
        AddCurrentDocument(db.Applications.Single());
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
        foreach (var application in db.Applications)
        {
            AddCurrentDocument(application);
        }

        await db.SaveChangesAsync();
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
        AddCurrentDocument(db.Applications.Single());
        await db.SaveChangesAsync();

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
        AddCurrentDocument(db.Applications.Single());
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
        AddCurrentDocument(db.Applications.Single());
        await db.SaveChangesAsync();

        Assert.Null(await service.GetDetailForAdminAsync(draft.Value!.PublicId, CancellationToken.None));
        Assert.True((await service.SubmitAsync("10000000146", draft.Value.PublicId, CancellationToken.None)).IsSuccess);
        Assert.NotNull(await service.GetDetailForAdminAsync(draft.Value.PublicId, CancellationToken.None));
    }

    [Fact]
    public async Task Workflow_application_cannot_be_approved_until_required_current_document_is_approved()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
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

    [Fact]
    public async Task Student_can_withdraw_submitted_application_and_reactivate_it_as_a_clean_draft()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        await ConfigureEvaluationAsync(db, offering, "10000000146");
        var service = CreateService(db);
        var draft = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        var application = db.Applications.Single();
        AddCurrentDocument(application);
        await db.SaveChangesAsync();
        Assert.True((await service.SubmitAsync(
            "10000000146",
            draft.Value!.PublicId,
            CancellationToken.None)).IsSuccess);
        application.CurrentStatus = ApplicationStatus.UnderReview.ToString();
        application.RowVersion = [1, 2, 3];
        await db.SaveChangesAsync();

        var beforeWithdrawal = await service.GetDetailForStudentAsync(
            "10000000146",
            application.PublicId,
            CancellationToken.None);
        var withdrawal = await service.WithdrawAsync(
            "10000000146",
            application.PublicId,
            new StudentApplicationCommandDto
            {
                RowVersion = Convert.ToBase64String(application.RowVersion)
            },
            CancellationToken.None);

        Assert.NotNull(beforeWithdrawal);
        Assert.True(beforeWithdrawal.CanWithdraw);
        Assert.False(beforeWithdrawal.CanReactivate);
        Assert.True(withdrawal.IsSuccess);
        Assert.Equal(ApplicationStatus.Withdrawn.ToString(), application.CurrentStatus);
        Assert.NotEmpty(db.ApplicationScoreSnapshots);
        Assert.NotEmpty(db.ApplicationEvaluations);
        Assert.Single(application.Documents, item => item.IsCurrent);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "ApplicationWithdrawnByStudent");

        var withdrawnDetail = await service.GetDetailForStudentAsync(
            "10000000146",
            application.PublicId,
            CancellationToken.None);
        var reactivation = await service.ReactivateAsync(
            "10000000146",
            application.PublicId,
            new StudentApplicationCommandDto
            {
                RowVersion = Convert.ToBase64String(application.RowVersion)
            },
            CancellationToken.None);

        Assert.NotNull(withdrawnDetail);
        Assert.False(withdrawnDetail.CanWithdraw);
        Assert.True(withdrawnDetail.CanReactivate);
        Assert.True(reactivation.IsSuccess);
        Assert.Equal(ApplicationStatus.Draft.ToString(), application.CurrentStatus);
        Assert.Empty(db.ApplicationScoreSnapshots);
        Assert.Empty(db.ApplicationEvaluations);
        Assert.Empty(db.ApplicationEvaluationComponents);
        Assert.Single(application.Documents, item => item.IsCurrent);
        Assert.Contains(db.ApplicationStatusHistories, item =>
            item.PreviousStatus == ApplicationStatus.UnderReview.ToString()
            && item.StatusName == ApplicationStatus.Withdrawn.ToString());
        Assert.Contains(db.ApplicationStatusHistories, item =>
            item.PreviousStatus == ApplicationStatus.Withdrawn.ToString()
            && item.StatusName == ApplicationStatus.Draft.ToString());
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "ApplicationReactivatedByStudent");
    }

    [Fact]
    public async Task Student_application_mutations_are_owner_scoped_and_blocked_after_deadline()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        var service = CreateService(db);
        var draft = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        var application = db.Applications.Single();
        application.CurrentStatus = ApplicationStatus.Pending.ToString();
        application.RowVersion = [4, 5, 6];
        await db.SaveChangesAsync();
        var command = new StudentApplicationCommandDto
        {
            RowVersion = Convert.ToBase64String(application.RowVersion)
        };

        var foreign = await service.WithdrawAsync(
            "10000000154",
            application.PublicId,
            command,
            CancellationToken.None);
        offering.ApplicationDeadlineUtc = new DateTime(2026, 7, 16, 23, 59, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();
        var expired = await service.WithdrawAsync(
            "10000000146",
            application.PublicId,
            command,
            CancellationToken.None);

        Assert.False(foreign.IsSuccess);
        Assert.Equal(StatusCodes.Status404NotFound, foreign.StatusCode);
        Assert.False(expired.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, expired.StatusCode);
        Assert.Equal(ApplicationStatus.Pending.ToString(), application.CurrentStatus);
        Assert.DoesNotContain(db.SecurityAuditLogs, item =>
            item.EventType is "ApplicationWithdrawnByStudent" or "ApplicationReactivatedByStudent");
    }

    [Fact]
    public async Task Terminal_or_finalized_application_never_exposes_student_lifecycle_capabilities()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        offering.UsesEvaluationWorkflow = true;
        offering.EvaluationState = OfferingEvaluationState.Finalized;
        var application = AddWorkflowApplication(
            db,
            offering,
            "10000000146",
            ApplicationStatus.Approved,
            true);
        application.UsesEvaluationWorkflow = true;
        application.RowVersion = [7, 8, 9];
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var detail = await service.GetDetailForStudentAsync(
            "10000000146",
            application.PublicId,
            CancellationToken.None);
        var withdrawal = await service.WithdrawAsync(
            "10000000146",
            application.PublicId,
            new StudentApplicationCommandDto
            {
                RowVersion = Convert.ToBase64String(application.RowVersion)
            },
            CancellationToken.None);

        Assert.NotNull(detail);
        Assert.False(detail.CanWithdraw);
        Assert.False(detail.CanReactivate);
        Assert.False(withdrawal.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, withdrawal.StatusCode);
        Assert.Equal(ApplicationStatus.Approved.ToString(), application.CurrentStatus);
    }

    [Fact]
    public async Task Read_only_invariant_audit_identifies_all_three_categories_and_excludes_valid_or_draft_records()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        var noSnapshots = AddWorkflowApplication(db, offering, "10000000146", ApplicationStatus.Pending, null);
        var noRequired = AddWorkflowApplication(db, offering, "10000000154", ApplicationStatus.UnderReview, false);
        var missingDocumentStudent = CreateStudent("10000000162", "missing@example.test");
        var validStudent = CreateStudent("10000000170", "valid@example.test");
        var draftStudent = CreateStudent("10000000189", "draft@example.test");
        var legacyStudent = CreateStudent("10000000197", "legacy@example.test");
        var withdrawnStudent = CreateStudent("10000000200", "withdrawn@example.test");
        db.Students.AddRange(
            missingDocumentStudent,
            validStudent,
            draftStudent,
            legacyStudent,
            withdrawnStudent);
        var missingDocument = AddWorkflowApplication(db, offering, missingDocumentStudent.Tc, ApplicationStatus.Approved, true);
        var valid = AddWorkflowApplication(db, offering, validStudent.Tc, ApplicationStatus.Rejected, true);
        AddCurrentDocument(valid);
        AddWorkflowApplication(db, offering, draftStudent.Tc, ApplicationStatus.Draft, null);
        AddWorkflowApplication(db, offering, legacyStudent.Tc, ApplicationStatus.Pending, null).UsesDocumentWorkflow = false;
        var withdrawn = AddWorkflowApplication(
            db,
            offering,
            withdrawnStudent.Tc,
            ApplicationStatus.Withdrawn,
            true);
        await db.SaveChangesAsync();
        var auditCount = db.SecurityAuditLogs.Count();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).GetDocumentWorkflowInvariantViolationsAsync(CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal("NoRequirementSnapshots", result.Single(item => item.ApplicationPublicId == noSnapshots.PublicId).ViolationCategory);
        Assert.Equal("NoRequiredRequirementSnapshots", result.Single(item => item.ApplicationPublicId == noRequired.PublicId).ViolationCategory);
        Assert.Equal("MissingRequiredDocuments", result.Single(item => item.ApplicationPublicId == missingDocument.PublicId).ViolationCategory);
        Assert.DoesNotContain(result, item => item.ApplicationPublicId == valid.PublicId);
        Assert.DoesNotContain(result, item => item.ApplicationPublicId == withdrawn.PublicId);
        Assert.Equal(auditCount, db.SecurityAuditLogs.Count());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static void AddRequirement(ProgramOffering offering, bool isRequired, string? code = null)
    {
        code ??= isRequired ? "TRANSCRIPT" : "PORTFOLIO";
        offering.DocumentRequirements.Add(new ProgramOfferingDocumentRequirement
        {
            PublicId = Guid.NewGuid(),
            DocumentCode = code,
            NormalizedDocumentCode = code,
            DisplayName = code == "TRANSCRIPT" ? "Transkript" : code,
            IsRequired = isRequired,
            IsActive = true,
            AllowedContentCategory = DocumentContentCategory.PdfOrImage,
            MaximumBytes = 1024 * 1024,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    private static void AddCurrentDocument(Application application)
    {
        var requirement = application.DocumentRequirementSnapshots.Single(item => item.IsRequired);
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

    private static Application AddWorkflowApplication(
        GraduateAppDbContext db,
        ProgramOffering offering,
        string studentTc,
        ApplicationStatus status,
        bool? snapshotIsRequired)
    {
        var application = new Application
        {
            PublicId = Guid.NewGuid(),
            Tc = studentTc,
            ProgramOfferingId = offering.ProgramOfferingId,
            ApplicationDate = DateTime.UtcNow,
            CurrentStatus = status.ToString(),
            UsesDocumentWorkflow = true
        };
        if (snapshotIsRequired.HasValue)
        {
            application.DocumentRequirementSnapshots.Add(new ApplicationDocumentRequirementSnapshot
            {
                PublicId = Guid.NewGuid(),
                DocumentCode = snapshotIsRequired.Value ? "TRANSCRIPT" : "PORTFOLIO",
                DisplayName = snapshotIsRequired.Value ? "Transkript" : "Portfolyo",
                IsRequired = snapshotIsRequired.Value,
                AllowedContentCategory = DocumentContentCategory.PdfOnly,
                MaximumBytes = 1024
            });
        }

        db.Applications.Add(application);
        return application;
    }

    private static void AssertSubmissionBlocked(
        GraduateAppDbContext db,
        Application application,
        string expectedReason)
    {
        Assert.Equal(ApplicationStatus.Draft.ToString(), application.CurrentStatus);
        Assert.DoesNotContain(db.SecurityAuditLogs, item => item.EventType == "ApplicationSubmitted");
        var audit = Assert.Single(db.SecurityAuditLogs.Where(item =>
            item.EventType == "DocumentSubmissionBlocked"
            && item.TargetId == application.PublicId.ToString("D")));
        using var details = JsonDocument.Parse(audit.Details!);
        var property = Assert.Single(details.RootElement.EnumerateObject());
        Assert.Equal("Reason", property.Name);
        Assert.Equal(expectedReason, property.Value.GetString());
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
        AddRequirement(offering, isRequired: true);
        db.ProgramOfferings.Add(offering);
        await db.SaveChangesAsync();
        return offering;
    }

    private static async Task ConfigureEvaluationAsync(
        GraduateAppDbContext db,
        ProgramOffering offering,
        string studentTc)
    {
        offering.UsesEvaluationWorkflow = true;
        var exam = new Exam { ExamName = "ALES" };
        offering.ExamRequirements.Add(new ProgramOfferingExamRequirement
        {
            Exam = exam,
            MinimumScore = 0m,
            IsRequired = true
        });
        offering.EvaluationCriteria.Add(new ProgramOfferingEvaluationCriterion
        {
            PublicId = Guid.NewGuid(),
            Code = "GPA",
            NormalizedCode = "GPA",
            DisplayName = "Lisans GNO",
            SourceType = EvaluationCriterionSourceType.UndergraduateGpa,
            WeightBasisPoints = 4000,
            MaximumRawScore = 4m,
            TieBreakPriority = 1
        });
        offering.EvaluationCriteria.Add(new ProgramOfferingEvaluationCriterion
        {
            PublicId = Guid.NewGuid(),
            Code = "ALES",
            NormalizedCode = "ALES",
            DisplayName = "ALES",
            SourceType = EvaluationCriterionSourceType.ExamScore,
            Exam = exam,
            WeightBasisPoints = 4000,
            MaximumRawScore = 100m,
            TieBreakPriority = 2
        });
        offering.EvaluationCriteria.Add(new ProgramOfferingEvaluationCriterion
        {
            PublicId = Guid.NewGuid(),
            Code = "INTERVIEW",
            NormalizedCode = "INTERVIEW",
            DisplayName = "Mülakat",
            SourceType = EvaluationCriterionSourceType.ManualScore,
            WeightBasisPoints = 2000,
            MaximumRawScore = 100m,
            TieBreakPriority = 3
        });
        var student = await db.Students.FindAsync(studentTc);
        student!.EducationInfos.Add(new EducationInfo
        {
            University = new University { UniversityName = "Test Üniversitesi" },
            Gno = 3.25m
        });
        db.StudentExamScores.Add(new StudentExamScore
        {
            Tc = studentTc,
            Exam = exam,
            Score = 80m,
            ExamDate = new DateOnly(2026, 1, 1)
        });
        await db.SaveChangesAsync();
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
