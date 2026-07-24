using System.Linq.Expressions;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GraduateApp.Tests;

public sealed class ApplicationEvaluationServiceTests
{
    [Fact]
    public async Task Admin_page_exposes_only_required_offering_exams_as_criterion_options()
    {
        await using var db = TestDb.Create();
        var (offering, _) = await SeedAsync(db);
        offering.ExamRequirements.Add(new ProgramOfferingExamRequirement
        {
            Exam = new Exam { ExamName = "ALES" },
            MinimumScore = 55.5m,
            IsRequired = true
        });
        offering.ExamRequirements.Add(new ProgramOfferingExamRequirement
        {
            Exam = new Exam { ExamName = "YDS" },
            MinimumScore = 70m,
            IsRequired = false
        });
        await db.SaveChangesAsync();

        var page = await Service(db).GetAdminPageAsync(
            offering.ProgramOfferingId,
            CancellationToken.None);

        var option = Assert.Single(Assert.IsType<AdminEvaluationPageDto>(page).EligibleExamRequirements);
        Assert.Equal("ALES", option.ExamName);
        Assert.Equal(55.5m, option.MinimumScore);
        Assert.True(option.IsRequired);
    }

    [Theory]
    [InlineData(OfferingEvaluationState.Configuring, false, true, true, true, false)]
    [InlineData(OfferingEvaluationState.Finalized, false, false, false, false, true)]
    [InlineData(OfferingEvaluationState.Published, false, false, false, false, false)]
    public async Task Admin_page_derives_capabilities_from_persisted_lifecycle(
        OfferingEvaluationState state,
        bool canEditPolicy,
        bool canDecideEligibility,
        bool canEditManualScore,
        bool canFinalize,
        bool canPublish)
    {
        await using var db = TestDb.Create();
        var (offering, _) = await SeedAsync(db);
        offering.EvaluationState = state;
        await db.SaveChangesAsync();

        var page = Assert.IsType<AdminEvaluationPageDto>(await Service(db).GetAdminPageAsync(
            offering.ProgramOfferingId,
            CancellationToken.None));

        Assert.Equal(canEditPolicy, page.Capabilities.CanEditPolicy);
        Assert.Equal(canDecideEligibility, page.Capabilities.CanDecideEligibility);
        Assert.Equal(canEditManualScore, page.Capabilities.CanEditManualScore);
        Assert.Equal(canFinalize, page.Capabilities.CanFinalize);
        Assert.Equal(canPublish, page.Capabilities.CanPublish);
    }

    [Fact]
    public async Task Eligibility_can_be_marked_eligible_only_after_required_documents_are_approved()
    {
        await using var db = TestDb.Create();
        var (_, application) = await SeedAsync(db, approvedDocument: true);

        var result = await Service(db).DecideEligibilityAsync(
            application.PublicId,
            7,
            new EligibilityDecisionDto
            {
                EligibilityStatus = EvaluationEligibilityStatus.Eligible,
                RowVersion = Token(application.Evaluation!.RowVersion)
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EvaluationEligibilityStatus.Eligible, application.Evaluation.EligibilityStatus);
        Assert.Equal(7, application.Evaluation.EligibilityDecidedByAdminId);
        Assert.Null(application.Evaluation.TotalScore);
        var audit = Assert.Single(db.SecurityAuditLogs.Where(item => item.EventType == "ApplicationEligibilityDecided"));
        Assert.Equal(7, audit.ActorAdminId);
        Assert.Equal(application.PublicId.ToString(), audit.TargetId);
        Assert.DoesNotContain(application.Tc, audit.Details!, StringComparison.Ordinal);
        Assert.DoesNotContain(application.TcNavigation.Email, audit.Details!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DocumentReviewStatus.Pending)]
    [InlineData(DocumentReviewStatus.Rejected)]
    public async Task Eligible_decision_is_blocked_until_current_required_document_is_approved(DocumentReviewStatus reviewStatus)
    {
        await using var db = TestDb.Create();
        var (_, application) = await SeedAsync(db, approvedDocument: false);
        application.DocumentRequirementSnapshots.Single().Documents.Single().ReviewStatus = reviewStatus;
        await db.SaveChangesAsync();

        var result = await Service(db).DecideEligibilityAsync(
            application.PublicId,
            7,
            new EligibilityDecisionDto
            {
                EligibilityStatus = EvaluationEligibilityStatus.Eligible,
                RowVersion = Token(application.Evaluation!.RowVersion)
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(EvaluationEligibilityStatus.Pending, application.Evaluation!.EligibilityStatus);
        Assert.DoesNotContain(db.SecurityAuditLogs, item => item.EventType == "ApplicationEligibilityDecided");
    }

    [Fact]
    public async Task Ineligible_decision_requires_reason_and_does_not_write_on_failure()
    {
        await using var db = TestDb.Create();
        var (_, application) = await SeedAsync(db);

        var result = await Service(db).DecideEligibilityAsync(
            application.PublicId,
            7,
            new EligibilityDecisionDto
            {
                EligibilityStatus = EvaluationEligibilityStatus.Ineligible,
                RowVersion = Token(application.Evaluation!.RowVersion)
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal(EvaluationEligibilityStatus.Pending, application.Evaluation!.EligibilityStatus);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task Eligibility_decision_requires_under_review_application()
    {
        await using var db = TestDb.Create();
        var (_, application) = await SeedAsync(db, manualRawScore: 80m);
        application.CurrentStatus = ApplicationStatus.Pending.ToString();
        await db.SaveChangesAsync();

        var result = await Service(db).DecideEligibilityAsync(
            application.PublicId,
            7,
            new EligibilityDecisionDto
            {
                EligibilityStatus = EvaluationEligibilityStatus.Eligible,
                RowVersion = Token(application.Evaluation!.RowVersion)
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(EvaluationEligibilityStatus.Pending, application.Evaluation!.EligibilityStatus);
    }

    [Fact]
    public async Task Ineligible_decision_trims_reason_and_excludes_candidate_from_ranking()
    {
        await using var db = TestDb.Create();
        var (offering, application) = await SeedAsync(db, manualRawScore: 100m);

        var result = await Service(db).DecideEligibilityAsync(
            application.PublicId,
            7,
            new EligibilityDecisionDto
            {
                EligibilityStatus = EvaluationEligibilityStatus.Ineligible,
                IneligibilityReason = "  Belge doğrulanamadı.  ",
                RowVersion = Token(application.Evaluation!.RowVersion)
            },
            CancellationToken.None);
        var preview = await Service(db).PreviewAsync(offering.ProgramOfferingId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Belge doğrulanamadı.", application.Evaluation!.IneligibilityReason);
        Assert.Empty(preview.Value!.Rows);
    }

    [Fact]
    public async Task Manual_score_is_normalized_weighted_and_audited()
    {
        await using var db = TestDb.Create();
        var (_, application) = await SeedAsync(db);
        var manual = application.Evaluation!.Components.Single(item =>
            item.SourceTypeSnapshot == EvaluationCriterionSourceType.ManualScore);

        var result = await Service(db).SetManualScoreAsync(
            application.PublicId,
            manual.CriterionPublicIdSnapshot,
            7,
            new ManualEvaluationScoreDto { RawScore = 82.34565m, RowVersion = Token(manual.RowVersion) },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(82.3457m, manual.NormalizedScore);
        Assert.Equal(32.9383m, manual.WeightedScore);
        Assert.Equal(7, manual.ManualScoredByAdminId);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "ApplicationManualScoreUpdated");
    }

    [Fact]
    public async Task Automatic_component_cannot_be_changed_through_manual_score_command()
    {
        await using var db = TestDb.Create();
        var (_, application) = await SeedAsync(db);
        var automatic = application.Evaluation!.Components.Single(item =>
            item.SourceTypeSnapshot == EvaluationCriterionSourceType.UndergraduateGpa);

        var result = await Service(db).SetManualScoreAsync(
            application.PublicId,
            automatic.CriterionPublicIdSnapshot,
            7,
            new ManualEvaluationScoreDto { RawScore = 90m, RowVersion = Token(automatic.RowVersion) },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(3.2m, automatic.RawScore);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(100.0001)]
    public async Task Manual_score_outside_zero_to_one_hundred_is_rejected_without_mutation(double value)
    {
        await using var db = TestDb.Create();
        var (_, application) = await SeedAsync(db);
        var manual = application.Evaluation!.Components.Single(item =>
            item.SourceTypeSnapshot == EvaluationCriterionSourceType.ManualScore);

        var result = await Service(db).SetManualScoreAsync(
            application.PublicId,
            manual.CriterionPublicIdSnapshot,
            7,
            new ManualEvaluationScoreDto
            {
                RawScore = (decimal)value,
                RowVersion = Token(manual.RowVersion)
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Null(manual.RawScore);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Theory]
    [InlineData(ApplicationStatus.Pending)]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public async Task Manual_score_requires_under_review_application(ApplicationStatus status)
    {
        await using var db = TestDb.Create();
        var (_, application) = await SeedAsync(db);
        application.CurrentStatus = status.ToString();
        await db.SaveChangesAsync();
        var manual = application.Evaluation!.Components.Single(item =>
            item.SourceTypeSnapshot == EvaluationCriterionSourceType.ManualScore);

        var result = await Service(db).SetManualScoreAsync(
            application.PublicId,
            manual.CriterionPublicIdSnapshot,
            7,
            new ManualEvaluationScoreDto { RawScore = 80m, RowVersion = Token(manual.RowVersion) },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    [Fact]
    public async Task Preview_reports_all_core_finalization_blockers()
    {
        await using var db = TestDb.Create();
        var (offering, application) = await SeedAsync(db);
        offering.IsOpen = true;
        offering.ApplicationDeadlineUtc = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);
        application.CurrentStatus = ApplicationStatus.Pending.ToString();
        await db.SaveChangesAsync();

        var result = await Service(db).PreviewAsync(offering.ProgramOfferingId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CanFinalize);
        Assert.Contains(result.Value.BlockingReasons, item => item.Contains("kapatılmalıdır", StringComparison.Ordinal));
        Assert.Contains(result.Value.BlockingReasons, item => item.Contains("henüz geçmedi", StringComparison.Ordinal));
        Assert.Contains(result.Value.BlockingReasons, item => item.Contains("bekleyen başvurular", StringComparison.Ordinal));
        Assert.Contains(result.Value.BlockingReasons, item => item.Contains("Uygunluk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Preview_uses_total_then_criterion_priority_then_application_date_for_deterministic_ranking()
    {
        await using var db = TestDb.Create();
        var (offering, first) = await SeedAsync(db, manualRawScore: 80m);
        first.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        Recalculate(first.Evaluation);
        var second = AddApplication(offering, "10000000154", "B", 3.0m, 87.5m, first.ApplicationDate.AddMinutes(-1));
        second.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        Recalculate(second.Evaluation);
        await db.SaveChangesAsync();

        var preview = (await Service(db).PreviewAsync(offering.ProgramOfferingId, CancellationToken.None)).Value!;

        Assert.True(preview.CanFinalize);
        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal(first.PublicId, preview.Rows[0].ApplicationPublicId);
        Assert.Equal(second.PublicId, preview.Rows[1].ApplicationPublicId);
    }

    [Fact]
    public async Task Preview_uses_application_date_then_public_id_when_scores_and_tie_breakers_are_equal()
    {
        await using var db = TestDb.Create();
        var (offering, latest) = await SeedAsync(db, manualRawScore: 80m);
        latest.PublicId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        latest.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        Recalculate(latest.Evaluation);
        var earlierDate = latest.ApplicationDate.AddMinutes(-1);
        var largerPublicId = AddApplication(offering, "10000000154", "B", 3.2m, 80m, earlierDate);
        largerPublicId.PublicId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        largerPublicId.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        Recalculate(largerPublicId.Evaluation);
        var smallerPublicId = AddApplication(offering, "10000000162", "C", 3.2m, 80m, earlierDate);
        smallerPublicId.PublicId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        smallerPublicId.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        Recalculate(smallerPublicId.Evaluation);
        await db.SaveChangesAsync();

        var preview = (await Service(db).PreviewAsync(offering.ProgramOfferingId, CancellationToken.None)).Value!;

        Assert.Equal(
            [smallerPublicId.PublicId, largerPublicId.PublicId, latest.PublicId],
            preview.Rows.Select(item => item.ApplicationPublicId));
    }

    [Fact]
    public async Task Finalize_assigns_quota_outcomes_but_does_not_publish_or_change_application_statuses()
    {
        await using var db = TestDb.Create();
        var (offering, first) = await SeedAsync(db, manualRawScore: 90m);
        offering.Quota = 1;
        first.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        Recalculate(first.Evaluation);
        var second = AddApplication(offering, "10000000154", "B", 2.5m, 60m, first.ApplicationDate.AddMinutes(1));
        second.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        Recalculate(second.Evaluation);
        var ineligible = AddApplication(offering, "10000000162", "C", 3.8m, 70m, first.ApplicationDate.AddMinutes(2));
        ineligible.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Ineligible;
        ineligible.Evaluation.IneligibilityReason = "Belge koşulu karşılanmadı.";
        await db.SaveChangesAsync();

        var result = await Service(db).FinalizeAsync(
            offering.ProgramOfferingId,
            7,
            new OfferingEvaluationCommandDto { RowVersion = Token(offering.RowVersion) },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OfferingEvaluationState.Finalized, offering.EvaluationState);
        Assert.Equal(EvaluationOutcome.Admitted, first.Evaluation.Outcome);
        Assert.Equal(EvaluationOutcome.NotAdmitted, second.Evaluation.Outcome);
        Assert.Equal(EvaluationOutcome.Ineligible, ineligible.Evaluation.Outcome);
        Assert.Equal(1, first.Evaluation.Rank);
        Assert.Equal(2, second.Evaluation.Rank);
        Assert.All(offering.Applications, item => Assert.Equal(ApplicationStatus.UnderReview.ToString(), item.CurrentStatus));
        Assert.Null(await Service(db).GetPublishedForStudentAsync(first.Tc, first.PublicId, CancellationToken.None));
    }

    [Fact]
    public async Task Publish_atomically_changes_statuses_creates_histories_and_exposes_only_owners_result()
    {
        await using var db = TestDb.Create();
        var (offering, admitted) = await SeedAsync(db, manualRawScore: 90m);
        offering.Quota = 1;
        admitted.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        Recalculate(admitted.Evaluation);
        var rejected = AddApplication(offering, "10000000154", "B", 2m, 50m, admitted.ApplicationDate.AddMinutes(1));
        rejected.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        Recalculate(rejected.Evaluation);
        await db.SaveChangesAsync();
        var service = Service(db);
        Assert.True((await service.FinalizeAsync(
            offering.ProgramOfferingId,
            7,
            new OfferingEvaluationCommandDto { RowVersion = Token(offering.RowVersion) },
            CancellationToken.None)).IsSuccess);

        var result = await service.PublishAsync(
            offering.ProgramOfferingId,
            7,
            new OfferingEvaluationCommandDto { RowVersion = Token(offering.RowVersion) },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OfferingEvaluationState.Published, offering.EvaluationState);
        Assert.Equal(ApplicationStatus.Approved.ToString(), admitted.CurrentStatus);
        Assert.Equal(ApplicationStatus.Rejected.ToString(), rejected.CurrentStatus);
        Assert.Equal(2, db.ApplicationStatusHistories.Count(item => item.Notes!.Contains("yayımlandı")));
        Assert.NotNull(await service.GetPublishedForStudentAsync(admitted.Tc, admitted.PublicId, CancellationToken.None));
        Assert.Null(await service.GetPublishedForStudentAsync(rejected.Tc, admitted.PublicId, CancellationToken.None));
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "OfferingResultsPublished");
    }

    [Fact]
    public async Task Publication_locks_eligibility_and_manual_scores_without_new_audits()
    {
        await using var db = TestDb.Create();
        var (offering, application) = await SeedAsync(db, manualRawScore: 90m);
        application.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        Recalculate(application.Evaluation);
        await db.SaveChangesAsync();
        var service = Service(db);
        Assert.True((await service.FinalizeAsync(
            offering.ProgramOfferingId,
            7,
            new OfferingEvaluationCommandDto { RowVersion = Token(offering.RowVersion) },
            CancellationToken.None)).IsSuccess);
        Assert.True((await service.PublishAsync(
            offering.ProgramOfferingId,
            7,
            new OfferingEvaluationCommandDto { RowVersion = Token(offering.RowVersion) },
            CancellationToken.None)).IsSuccess);
        var manual = application.Evaluation.Components.Single(item =>
            item.SourceTypeSnapshot == EvaluationCriterionSourceType.ManualScore);
        var auditCount = db.SecurityAuditLogs.Count();

        var eligibility = await service.DecideEligibilityAsync(
            application.PublicId,
            7,
            new EligibilityDecisionDto
            {
                EligibilityStatus = EvaluationEligibilityStatus.Ineligible,
                IneligibilityReason = "Sonradan değiştirilemez.",
                RowVersion = Token(application.Evaluation.RowVersion)
            },
            CancellationToken.None);
        var score = await service.SetManualScoreAsync(
            application.PublicId,
            manual.CriterionPublicIdSnapshot,
            7,
            new ManualEvaluationScoreDto { RawScore = 10m, RowVersion = Token(manual.RowVersion) },
            CancellationToken.None);

        Assert.False(eligibility.IsSuccess);
        Assert.False(score.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, eligibility.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, score.StatusCode);
        Assert.Equal(EvaluationEligibilityStatus.Eligible, application.Evaluation.EligibilityStatus);
        Assert.Equal(90m, manual.RawScore);
        Assert.Equal(auditCount, db.SecurityAuditLogs.Count());
    }

    [Theory]
    [InlineData(OfferingEvaluationState.Finalized)]
    [InlineData(OfferingEvaluationState.Published)]
    public async Task Locked_lifecycle_rejects_eligibility_and_manual_score_without_data_history_or_audit(
        OfferingEvaluationState state)
    {
        await using var db = TestDb.Create();
        var (offering, application) = await SeedAsync(db, manualRawScore: 90m);
        application.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        Recalculate(application.Evaluation);
        offering.EvaluationState = state;
        await db.SaveChangesAsync();
        var service = Service(db);
        var manual = application.Evaluation.Components.Single(item =>
            item.SourceTypeSnapshot == EvaluationCriterionSourceType.ManualScore);
        var originalEligibility = application.Evaluation.EligibilityStatus;
        var originalTotal = application.Evaluation.TotalScore;
        var originalScore = manual.RawScore;
        var originalHistoryCount = db.ApplicationStatusHistories.Count();
        var originalAuditCount = db.SecurityAuditLogs.Count();

        var eligibility = await service.DecideEligibilityAsync(
            application.PublicId,
            7,
            new EligibilityDecisionDto
            {
                EligibilityStatus = EvaluationEligibilityStatus.Ineligible,
                IneligibilityReason = "Lifecycle kilidi.",
                RowVersion = Token(application.Evaluation.RowVersion)
            },
            CancellationToken.None);
        var score = await service.SetManualScoreAsync(
            application.PublicId,
            manual.CriterionPublicIdSnapshot,
            7,
            new ManualEvaluationScoreDto { RawScore = 10m, RowVersion = Token(manual.RowVersion) },
            CancellationToken.None);

        Assert.False(eligibility.IsSuccess);
        Assert.False(score.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, eligibility.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, score.StatusCode);
        Assert.Equal(originalEligibility, application.Evaluation.EligibilityStatus);
        Assert.Equal(originalTotal, application.Evaluation.TotalScore);
        Assert.Equal(originalScore, manual.RawScore);
        Assert.Equal(originalHistoryCount, db.ApplicationStatusHistories.Count());
        Assert.Equal(originalAuditCount, db.SecurityAuditLogs.Count());
    }

    [Fact]
    public async Task Repeated_finalize_and_publish_are_safe_conflicts_without_duplicate_audits()
    {
        await using var db = TestDb.Create();
        var (offering, application) = await SeedAsync(db, manualRawScore: 90m);
        application.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        Recalculate(application.Evaluation);
        await db.SaveChangesAsync();
        var service = Service(db);
        var command = new OfferingEvaluationCommandDto { RowVersion = Token(offering.RowVersion) };

        Assert.True((await service.FinalizeAsync(offering.ProgramOfferingId, 7, command, CancellationToken.None)).IsSuccess);
        Assert.False((await service.FinalizeAsync(offering.ProgramOfferingId, 7, command, CancellationToken.None)).IsSuccess);
        Assert.True((await service.PublishAsync(offering.ProgramOfferingId, 7, command, CancellationToken.None)).IsSuccess);
        Assert.False((await service.PublishAsync(offering.ProgramOfferingId, 7, command, CancellationToken.None)).IsSuccess);

        Assert.Single(db.SecurityAuditLogs.Where(item => item.EventType == "OfferingEvaluationFinalized"));
        Assert.Single(db.SecurityAuditLogs.Where(item => item.EventType == "OfferingResultsPublished"));
    }

    [Fact]
    public async Task Failed_finalize_and_unfinalized_publish_leave_no_partial_result_or_audit()
    {
        await using var db = TestDb.Create();
        var (offering, application) = await SeedAsync(db);
        application.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        await db.SaveChangesAsync();
        var service = Service(db);
        var command = new OfferingEvaluationCommandDto { RowVersion = Token(offering.RowVersion) };

        var finalize = await service.FinalizeAsync(offering.ProgramOfferingId, 7, command, CancellationToken.None);
        var publish = await service.PublishAsync(offering.ProgramOfferingId, 7, command, CancellationToken.None);

        Assert.False(finalize.IsSuccess);
        Assert.False(publish.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, finalize.StatusCode);
        Assert.Null(application.Evaluation.Rank);
        Assert.Null(application.Evaluation.Outcome);
        Assert.Null(application.Evaluation.FinalizedAtUtc);
        Assert.Equal(OfferingEvaluationState.Configuring, offering.EvaluationState);
        Assert.DoesNotContain(db.SecurityAuditLogs, item =>
            item.EventType is "OfferingEvaluationFinalized" or "OfferingResultsPublished");
    }

    [Fact]
    public async Task Draft_and_withdrawn_applications_are_not_ranked_or_published()
    {
        await using var db = TestDb.Create();
        var (offering, candidate) = await SeedAsync(db, manualRawScore: 90m);
        candidate.Evaluation!.EligibilityStatus = EvaluationEligibilityStatus.Eligible;
        Recalculate(candidate.Evaluation);
        var draft = new Application
        {
            PublicId = Guid.NewGuid(),
            Tc = "10000000154",
            TcNavigation = Student("10000000154", "B"),
            ApplicationDate = candidate.ApplicationDate.AddMinutes(-2),
            CurrentStatus = ApplicationStatus.Draft.ToString(),
            UsesDocumentWorkflow = true,
            UsesEvaluationWorkflow = true
        };
        offering.Applications.Add(draft);
        var withdrawn = AddApplication(offering, "10000000162", "C", 4m, 100m, candidate.ApplicationDate.AddMinutes(-1));
        withdrawn.CurrentStatus = ApplicationStatus.Withdrawn.ToString();
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.True((await service.FinalizeAsync(
            offering.ProgramOfferingId,
            7,
            new OfferingEvaluationCommandDto { RowVersion = Token(offering.RowVersion) },
            CancellationToken.None)).IsSuccess);
        Assert.True((await service.PublishAsync(
            offering.ProgramOfferingId,
            7,
            new OfferingEvaluationCommandDto { RowVersion = Token(offering.RowVersion) },
            CancellationToken.None)).IsSuccess);

        Assert.Equal(ApplicationStatus.Draft.ToString(), draft.CurrentStatus);
        Assert.Equal(ApplicationStatus.Withdrawn.ToString(), withdrawn.CurrentStatus);
        Assert.Null(withdrawn.Evaluation!.Outcome);
    }

    [Fact]
    public async Task Finalize_does_not_convert_request_cancellation_to_concurrency_conflict()
    {
        await using var db = TestDb.Create(new ThrowingQueryExpressionInterceptor(
            () => new OperationCanceledException("İstek iptal edildi.")));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Service(db).FinalizeAsync(
                1,
                7,
                new OfferingEvaluationCommandDto { RowVersion = "AQ==" },
                CancellationToken.None));
    }

    [Fact]
    public async Task Finalize_does_not_hide_non_deadlock_query_exception_as_concurrency_conflict()
    {
        await using var db = TestDb.Create(new ThrowingQueryExpressionInterceptor(
            () => new InvalidOperationException("Beklenmeyen sorgu hatası.")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(db).FinalizeAsync(
                1,
                7,
                new OfferingEvaluationCommandDto { RowVersion = "AQ==" },
                CancellationToken.None));

        Assert.Equal("Beklenmeyen sorgu hatası.", exception.Message);
    }

    private static ApplicationEvaluationService Service(GraduateAppDbContext db) =>
        new(db, new TestTimeProvider(new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)));

    private static string Token(byte[] value) => Convert.ToBase64String(value);

    private static async Task<(ProgramOffering Offering, Application Application)> SeedAsync(
        GraduateAppDbContext db,
        bool approvedDocument = true,
        decimal? manualRawScore = null)
    {
        var offering = new ProgramOffering
        {
            Program = new GraduateApp.API.Models.Program
            {
                ProgramName = "Test Programı",
                DegreeType = "Tezli Yüksek Lisans",
                IsActive = true,
                Institute = new Institute { InstituteName = "Test Enstitüsü", IsActive = true }
            },
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall,
            ApplicationStartUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            ApplicationDeadlineUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Quota = 2,
            IsOpen = false,
            UsesEvaluationWorkflow = true,
            EvaluationState = OfferingEvaluationState.Configuring
        };
        AddCriteria(offering);
        db.ProgramOfferings.Add(offering);
        var application = AddApplication(offering, "10000000146", "A", 3.2m, manualRawScore, new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc));
        AddApprovedDocument(application, approvedDocument);
        await db.SaveChangesAsync();
        return (offering, application);
    }

    private static void AddCriteria(ProgramOffering offering)
    {
        offering.EvaluationCriteria.Add(new ProgramOfferingEvaluationCriterion
        {
            PublicId = Guid.NewGuid(),
            Code = "GPA",
            NormalizedCode = "GPA",
            DisplayName = "Lisans GNO",
            SourceType = EvaluationCriterionSourceType.UndergraduateGpa,
            WeightBasisPoints = 6000,
            MaximumRawScore = 4m,
            TieBreakPriority = 1
        });
        offering.EvaluationCriteria.Add(new ProgramOfferingEvaluationCriterion
        {
            PublicId = Guid.NewGuid(),
            Code = "MANUAL",
            NormalizedCode = "MANUAL",
            DisplayName = "Mülakat",
            SourceType = EvaluationCriterionSourceType.ManualScore,
            WeightBasisPoints = 4000,
            MaximumRawScore = 100m,
            TieBreakPriority = 2
        });
    }

    private static Application AddApplication(
        ProgramOffering offering,
        string tc,
        string surname,
        decimal gpa,
        decimal? manual,
        DateTime applicationDate)
    {
        var gpaCriterion = offering.EvaluationCriteria.Single(item => item.SourceType == EvaluationCriterionSourceType.UndergraduateGpa);
        var manualCriterion = offering.EvaluationCriteria.Single(item => item.SourceType == EvaluationCriterionSourceType.ManualScore);
        var gpaNormalized = EvaluationScoring.Normalize(gpa, 4m);
        var manualNormalized = manual.HasValue ? EvaluationScoring.Normalize(manual.Value, 100m) : (decimal?)null;
        var application = new Application
        {
            PublicId = Guid.NewGuid(),
            Tc = tc,
            TcNavigation = Student(tc, surname),
            ApplicationDate = applicationDate,
            CurrentStatus = ApplicationStatus.UnderReview.ToString(),
            UsesDocumentWorkflow = true,
            UsesEvaluationWorkflow = true,
            Evaluation = new ApplicationEvaluation
            {
                ProgramOffering = offering,
                EligibilityStatus = EvaluationEligibilityStatus.Pending,
                Components =
                [
                    new ApplicationEvaluationComponent
                    {
                        SourceCriterion = gpaCriterion,
                        CriterionPublicIdSnapshot = gpaCriterion.PublicId,
                        CodeSnapshot = gpaCriterion.Code,
                        DisplayNameSnapshot = gpaCriterion.DisplayName,
                        SourceTypeSnapshot = gpaCriterion.SourceType,
                        RawScore = gpa,
                        MaximumRawScoreSnapshot = 4m,
                        NormalizedScore = gpaNormalized,
                        WeightBasisPointsSnapshot = 6000,
                        WeightedScore = EvaluationScoring.Weight(gpaNormalized, 6000),
                        TieBreakPrioritySnapshot = 1
                    },
                    new ApplicationEvaluationComponent
                    {
                        SourceCriterion = manualCriterion,
                        CriterionPublicIdSnapshot = manualCriterion.PublicId,
                        CodeSnapshot = manualCriterion.Code,
                        DisplayNameSnapshot = manualCriterion.DisplayName,
                        SourceTypeSnapshot = manualCriterion.SourceType,
                        RawScore = manual,
                        MaximumRawScoreSnapshot = 100m,
                        NormalizedScore = manualNormalized,
                        WeightBasisPointsSnapshot = 4000,
                        WeightedScore = manualNormalized.HasValue ? EvaluationScoring.Weight(manualNormalized.Value, 4000) : null,
                        TieBreakPrioritySnapshot = 2
                    }
                ]
            }
        };
        offering.Applications.Add(application);
        return application;
    }

    private static Student Student(string tc, string surname) => new()
    {
        Tc = tc,
        PublicId = Guid.NewGuid(),
        StudentName = "Aday",
        StudentSurname = surname,
        Email = $"{tc}@example.test",
        NormalizedEmail = $"{tc}@EXAMPLE.TEST",
        PasswordHash = "hash",
        SecurityStamp = Guid.NewGuid().ToString("N"),
        IsActive = true
    };

    private static void AddApprovedDocument(Application application, bool approved)
    {
        application.DocumentRequirementSnapshots.Add(new ApplicationDocumentRequirementSnapshot
        {
            PublicId = Guid.NewGuid(),
            DocumentCode = "TRANSCRIPT",
            DisplayName = "Transkript",
            IsRequired = true,
            AllowedContentCategory = DocumentContentCategory.PdfOnly,
            MaximumBytes = 1024,
            Documents =
            [
                new ApplicationDocument
                {
                    PublicId = Guid.NewGuid(),
                    VersionNumber = 1,
                    IsCurrent = true,
                    OriginalFileName = "transkript.pdf",
                    ObjectKey = Guid.NewGuid().ToString("N"),
                    VerifiedContentType = "application/pdf",
                    FileSize = 100,
                    Sha256 = new string('0', 64),
                    ReviewStatus = approved ? DocumentReviewStatus.Approved : DocumentReviewStatus.Pending,
                    UploadedAtUtc = DateTime.UtcNow
                }
            ]
        });
    }

    private static void Recalculate(ApplicationEvaluation evaluation)
    {
        evaluation.TotalScore = EvaluationScoring.Total(
            evaluation.Components.Select(item => item.WeightedScore!.Value));
    }

    private sealed class ThrowingQueryExpressionInterceptor(Func<Exception> exceptionFactory)
        : IQueryExpressionInterceptor
    {
        public Expression QueryCompilationStarting(
            Expression queryExpression,
            QueryExpressionEventData eventData) =>
            throw exceptionFactory();
    }
}
