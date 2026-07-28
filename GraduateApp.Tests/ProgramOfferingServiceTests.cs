using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GraduateApp.Tests;

public sealed class ProgramOfferingServiceTests
{
    [Fact]
    public async Task Create_rejects_an_open_offering_and_does_not_persist_it()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var request = CreateRequest(program.ProgramId, AcademicTerm.Fall, isOpen: true);

        var result = await CreateService(db).CreateAsync(1, request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(
            "Yeni ilan önce kapalı oluşturulmalıdır. En az bir zorunlu belge koşulu tanımlandıktan sonra ilanı açabilirsiniz.",
            result.Error);
        Assert.Empty(db.ProgramOfferings);
        Assert.DoesNotContain(db.SecurityAuditLogs, item => item.EventType == "ProgramOfferingCreated");
    }

    [Fact]
    public async Task Create_always_persists_a_closed_offering()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);

        var result = await CreateService(db).CreateAsync(
            1,
            CreateRequest(program.ProgramId, AcademicTerm.Fall),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsOpen);
        Assert.True(result.Value.UsesEvaluationWorkflow);
        Assert.False(db.ProgramOfferings.Single().IsOpen);
    }

    [Fact]
    public async Task Create_rejects_duplicate_program_year_term_but_allows_another_term()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var service = CreateService(db);

        var first = await service.CreateAsync(1, CreateRequest(program.ProgramId, AcademicTerm.Fall), CancellationToken.None);
        var duplicate = await service.CreateAsync(1, CreateRequest(program.ProgramId, AcademicTerm.Fall), CancellationToken.None);
        var spring = await service.CreateAsync(1, CreateRequest(program.ProgramId, AcademicTerm.Spring), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, duplicate.StatusCode);
        Assert.True(spring.IsSuccess);
        Assert.Equal(2, db.ProgramOfferings.Count());
    }

    [Fact]
    public async Task Admin_list_filters_by_academic_year_and_term()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var service = CreateService(db);
        await service.CreateAsync(1, CreateRequest(program.ProgramId, AcademicTerm.Fall), CancellationToken.None);
        await service.CreateAsync(1, CreateRequest(program.ProgramId, AcademicTerm.Spring), CancellationToken.None);
        await service.CreateAsync(1, CreateRequest(program.ProgramId, AcademicTerm.Fall, 2027), CancellationToken.None);

        var result = await service.GetForAdminAsync(2026, AcademicTerm.Fall, false, CancellationToken.None);

        var offering = Assert.Single(result);
        Assert.Equal(2026, offering.AcademicYearStart);
        Assert.Equal(AcademicTerm.Fall, offering.Term);
    }

    [Fact]
    public async Task Admin_list_returns_document_requirement_count_and_active_required_summary()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var offering = AddOffering(db, program.ProgramId);
        offering.DocumentRequirements.Add(DocumentRequirement(isRequired: true, isActive: true));
        offering.DocumentRequirements.Add(DocumentRequirement(isRequired: false, isActive: true));
        offering.DocumentRequirements.Add(DocumentRequirement(isRequired: true, isActive: false));
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetForAdminAsync(
            2026,
            AcademicTerm.Fall,
            includeArchived: false,
            CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal(3, dto.DocumentRequirementCount);
        Assert.Equal(1, dto.ActiveRequiredDocumentRequirementCount);
        Assert.True(dto.HasActiveRequiredDocumentRequirement);
    }

    [Fact]
    public async Task Admin_health_list_classifies_historical_open_offerings_without_writes_or_audits()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);

        var legacy = AddOffering(db, program.ProgramId);
        legacy.AcademicYearStart = 0;
        legacy.Term = AcademicTerm.LegacyUnspecified;
        legacy.IsOpen = true;

        var healthy = AddOffering(db, program.ProgramId);
        healthy.AcademicYearStart = 2021;
        healthy.IsOpen = true;
        healthy.DocumentRequirements.Add(DocumentRequirement(isRequired: true, isActive: true));

        var noApplications = AddOffering(db, program.ProgramId);
        noApplications.AcademicYearStart = 2022;
        noApplications.IsOpen = true;
        noApplications.DocumentRequirements.Add(DocumentRequirement(isRequired: false, isActive: true));

        var withDraft = AddOffering(db, program.ProgramId);
        withDraft.AcademicYearStart = 2023;
        withDraft.IsOpen = true;
        withDraft.Applications.Add(CreateApplication("10000000146", ApplicationStatus.Draft));

        var withSubmitted = AddOffering(db, program.ProgramId);
        withSubmitted.AcademicYearStart = 2024;
        withSubmitted.IsOpen = true;
        withSubmitted.UsesEvaluationWorkflow = true;
        withSubmitted.EvaluationState = OfferingEvaluationState.Published;
        withSubmitted.Applications.Add(CreateApplication("10000000147", ApplicationStatus.Draft));
        withSubmitted.Applications.Add(CreateApplication("10000000148", ApplicationStatus.Pending));
        withSubmitted.Applications.Add(CreateApplication("10000000149", ApplicationStatus.UnderReview));
        withSubmitted.Applications.Add(CreateApplication("10000000150", ApplicationStatus.Approved));
        withSubmitted.Applications.Add(CreateApplication("10000000151", ApplicationStatus.Rejected));
        withSubmitted.Applications.Add(CreateApplication("10000000152", ApplicationStatus.Withdrawn));

        var closed = AddOffering(db, program.ProgramId);
        closed.AcademicYearStart = 2025;
        await db.SaveChangesAsync();
        var auditCount = await db.SecurityAuditLogs.CountAsync();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).GetForAdminAsync(
            academicYearStart: null,
            term: null,
            includeArchived: true,
            CancellationToken.None);

        var byYear = result.ToDictionary(item => item.AcademicYearStart);
        Assert.Equal(
            OfferingDocumentConfigurationHealth.LegacyOutsideDocumentWorkflow,
            byYear[0].DocumentConfigurationHealth);
        Assert.False(byYear[0].UsesDocumentWorkflow);
        Assert.Equal(
            OfferingDocumentConfigurationHealth.OpenHealthy,
            byYear[2021].DocumentConfigurationHealth);
        Assert.Equal(
            OfferingDocumentConfigurationHealth.OpenInvalidNoApplications,
            byYear[2022].DocumentConfigurationHealth);
        Assert.Equal(1, byYear[2022].DocumentRequirementCount);
        Assert.Equal(0, byYear[2022].ActiveRequiredDocumentRequirementCount);
        Assert.Equal(
            OfferingDocumentConfigurationHealth.OpenInvalidWithDrafts,
            byYear[2023].DocumentConfigurationHealth);
        Assert.Equal(1, byYear[2023].DraftApplicationCount);
        Assert.Equal(0, byYear[2023].SubmittedOrLaterApplicationCount);
        Assert.Equal(
            OfferingDocumentConfigurationHealth.OpenInvalidWithSubmittedApplications,
            byYear[2024].DocumentConfigurationHealth);
        Assert.Equal(1, byYear[2024].DraftApplicationCount);
        Assert.Equal(5, byYear[2024].SubmittedOrLaterApplicationCount);
        Assert.Equal(
            OfferingDocumentConfigurationHealth.ClosedWorkflow,
            byYear[2025].DocumentConfigurationHealth);
        Assert.All(result.Where(item => item.UsesDocumentWorkflow), item =>
            Assert.Equal("Fen Bilimleri", item.InstituteName));
        Assert.Equal(auditCount, await db.SecurityAuditLogs.CountAsync());
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Create_rejects_non_utc_dates()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var request = CreateRequest(program.ProgramId, AcademicTerm.Fall);
        request = new ProgramOfferingCreateDto
        {
            ProgramId = request.ProgramId,
            AcademicYearStart = request.AcademicYearStart,
            Term = request.Term,
            ApplicationStartUtc = DateTime.SpecifyKind(request.ApplicationStartUtc, DateTimeKind.Unspecified),
            ApplicationDeadlineUtc = request.ApplicationDeadlineUtc,
            Quota = request.Quota
        };

        var result = await CreateService(db).CreateAsync(1, request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Inactive_institute_excludes_program_from_catalog_and_blocks_new_offering()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        program.Institute.IsActive = false;
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var catalog = await service.GetCatalogAsync(CancellationToken.None);
        var result = await service.CreateAsync(
            1,
            CreateRequest(program.ProgramId, AcademicTerm.Fall),
            CancellationToken.None);

        Assert.DoesNotContain(catalog.Programs, item => item.ProgramId == program.ProgramId);
        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Empty(db.ProgramOfferings);
    }

    [Fact]
    public async Task Update_returns_safe_conflict_when_optimistic_concurrency_fails()
    {
        var interceptor = new SwitchableConcurrencyInterceptor();
        await using var db = TestDb.Create(interceptor);
        var program = await SeedProgramAsync(db);
        var offering = new ProgramOffering
        {
            ProgramId = program.ProgramId,
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall,
            ApplicationStartUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            ApplicationDeadlineUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Quota = 10,
            IsOpen = false
        };
        db.ProgramOfferings.Add(offering);
        await db.SaveChangesAsync();
        interceptor.Enabled = true;

        var result = await CreateService(db).UpdateAsync(
            offering.ProgramOfferingId,
            1,
            new ProgramOfferingUpdateDto
            {
                ProgramId = program.ProgramId,
                AcademicYearStart = 2026,
                Term = AcademicTerm.Fall,
                ApplicationStartUtc = offering.ApplicationStartUtc!.Value,
                ApplicationDeadlineUtc = offering.ApplicationDeadlineUtc!.Value,
                Quota = offering.Quota,
                RowVersion = Convert.ToBase64String(offering.RowVersion)
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.DoesNotContain("RowVersion", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Update_cannot_open_without_an_active_required_document_requirement(
        bool addOptionalRequirement,
        bool addInactiveRequiredRequirement)
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var offering = AddOffering(db, program.ProgramId);
        if (addOptionalRequirement || addInactiveRequiredRequirement)
        {
            offering.DocumentRequirements.Add(DocumentRequirement(
                isRequired: addInactiveRequiredRequirement,
                isActive: !addInactiveRequiredRequirement));
        }

        await db.SaveChangesAsync();

        var result = await CreateService(db).UpdateAsync(
            offering.ProgramOfferingId,
            1,
            UpdateRequest(offering, isOpen: true),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(
            "İlan açılmadan önce en az bir aktif ve zorunlu belge koşulu tanımlayın.",
            result.Error);
        Assert.False(offering.IsOpen);
    }

    [Fact]
    public async Task Update_can_open_when_an_active_required_document_requirement_exists()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var offering = AddOffering(db, program.ProgramId);
        offering.DocumentRequirements.Add(DocumentRequirement(isRequired: true, isActive: true));
        await db.SaveChangesAsync();

        var result = await CreateService(db).UpdateAsync(
            offering.ProgramOfferingId,
            1,
            UpdateRequest(offering, isOpen: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsOpen);
        Assert.True(db.ProgramOfferings.Single().IsOpen);
    }

    [Fact]
    public async Task Evaluation_offering_cannot_open_until_policy_weights_equal_ten_thousand()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var offering = AddOffering(db, program.ProgramId);
        offering.UsesEvaluationWorkflow = true;
        offering.DocumentRequirements.Add(DocumentRequirement(isRequired: true, isActive: true));
        offering.EvaluationCriteria.Add(new ProgramOfferingEvaluationCriterion
        {
            PublicId = Guid.NewGuid(),
            Code = "MANUAL",
            NormalizedCode = "MANUAL",
            DisplayName = "Mülakat",
            SourceType = EvaluationCriterionSourceType.ManualScore,
            WeightBasisPoints = 9999,
            MaximumRawScore = 100m,
            TieBreakPriority = 1
        });
        await db.SaveChangesAsync();

        var blocked = await CreateService(db).UpdateAsync(
            offering.ProgramOfferingId,
            1,
            UpdateRequest(offering, isOpen: true),
            CancellationToken.None);
        offering.EvaluationCriteria.Single().WeightBasisPoints = 10000;
        await db.SaveChangesAsync();
        var opened = await CreateService(db).UpdateAsync(
            offering.ProgramOfferingId,
            1,
            UpdateRequest(offering, isOpen: true),
            CancellationToken.None);

        Assert.False(blocked.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, blocked.StatusCode);
        Assert.True(opened.IsSuccess);
    }

    [Theory]
    [InlineData(OrphanedExamRequirement.OptionalMatchingExam)]
    [InlineData(OrphanedExamRequirement.MissingMatchingExam)]
    [InlineData(OrphanedExamRequirement.DifferentRequiredExam)]
    public async Task Evaluation_offering_cannot_open_when_proposed_requirements_orphan_exam_criterion(
        OrphanedExamRequirement orphanedRequirement)
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var offering = AddOffering(db, program.ProgramId);
        var (ales, yds) = AddValidExamEvaluationPolicy(db, offering);
        offering.DocumentRequirements.Add(DocumentRequirement(isRequired: true, isActive: true));
        await db.SaveChangesAsync();
        var originalRowVersion = offering.RowVersion.ToArray();
        var originalAuditCount = db.SecurityAuditLogs.Count();
        var proposedRequirements = orphanedRequirement switch
        {
            OrphanedExamRequirement.OptionalMatchingExam =>
                new[] { Requirement(ales.ExamId, isRequired: false) },
            OrphanedExamRequirement.MissingMatchingExam =>
                [],
            OrphanedExamRequirement.DifferentRequiredExam =>
                new[] { Requirement(yds.ExamId, isRequired: true) },
            _ => throw new ArgumentOutOfRangeException(nameof(orphanedRequirement))
        };

        var result = await CreateService(db).UpdateAsync(
            offering.ProgramOfferingId,
            1,
            UpdateRequest(offering, isOpen: true, examRequirements: proposedRequirements),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(
            "İlan açılamaz: ALES değerlendirme kriteri için zorunlu sınav koşulu bulunmalıdır.",
            result.Error);
        Assert.False(offering.IsOpen);
        var storedRequirement = Assert.Single(offering.ExamRequirements);
        Assert.Equal(ales.ExamId, storedRequirement.ExamId);
        Assert.True(storedRequirement.IsRequired);
        Assert.Equal(originalRowVersion, offering.RowVersion);
        Assert.Equal(originalAuditCount, db.SecurityAuditLogs.Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Evaluation_offering_can_open_with_required_matching_exam_and_optional_other_exams(
        bool includeOptionalOtherExam)
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var offering = AddOffering(db, program.ProgramId);
        var (ales, yds) = AddValidExamEvaluationPolicy(db, offering);
        offering.DocumentRequirements.Add(DocumentRequirement(isRequired: true, isActive: true));
        await db.SaveChangesAsync();
        var proposedRequirements = new List<ProgramOfferingRequirementInputDto>
        {
            Requirement(ales.ExamId, isRequired: true)
        };
        if (includeOptionalOtherExam)
        {
            proposedRequirements.Add(Requirement(yds.ExamId, isRequired: false));
        }

        var result = await CreateService(db).UpdateAsync(
            offering.ProgramOfferingId,
            1,
            UpdateRequest(offering, isOpen: true, examRequirements: proposedRequirements),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsOpen);
        Assert.Contains(result.Value.ExamRequirements, item => item.ExamId == ales.ExamId && item.IsRequired);
        if (includeOptionalOtherExam)
        {
            Assert.Contains(result.Value.ExamRequirements, item => item.ExamId == yds.ExamId && !item.IsRequired);
        }
    }

    [Fact]
    public async Task Closed_evaluation_offering_can_temporarily_store_orphaned_exam_criterion()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var offering = AddOffering(db, program.ProgramId);
        var (ales, _) = AddValidExamEvaluationPolicy(db, offering);
        await db.SaveChangesAsync();

        var result = await CreateService(db).UpdateAsync(
            offering.ProgramOfferingId,
            1,
            UpdateRequest(
                offering,
                isOpen: false,
                examRequirements: [Requirement(ales.ExamId, isRequired: false)]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsOpen);
        Assert.False(Assert.Single(result.Value.ExamRequirements).IsRequired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Open_evaluation_offering_cannot_remove_or_make_criterion_exam_optional(bool removeRequirement)
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var offering = AddOffering(db, program.ProgramId);
        var (ales, _) = AddValidExamEvaluationPolicy(db, offering);
        offering.DocumentRequirements.Add(DocumentRequirement(isRequired: true, isActive: true));
        offering.IsOpen = true;
        await db.SaveChangesAsync();
        var originalRowVersion = offering.RowVersion.ToArray();
        var originalAuditCount = db.SecurityAuditLogs.Count();
        ProgramOfferingRequirementInputDto[] proposedRequirements = removeRequirement
            ? []
            : [Requirement(ales.ExamId, isRequired: false)];

        var result = await CreateService(db).UpdateAsync(
            offering.ProgramOfferingId,
            1,
            UpdateRequest(offering, isOpen: true, examRequirements: proposedRequirements),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.True(offering.IsOpen);
        Assert.True(Assert.Single(offering.ExamRequirements).IsRequired);
        Assert.Equal(originalRowVersion, offering.RowVersion);
        Assert.Equal(originalAuditCount, db.SecurityAuditLogs.Count());
    }

    [Theory]
    [InlineData(OfferingEvaluationState.Finalized)]
    [InlineData(OfferingEvaluationState.Published)]
    public async Task Invalid_open_finalized_or_published_offering_can_only_be_closed_for_remediation(
        OfferingEvaluationState state)
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var offering = AddOffering(db, program.ProgramId);
        offering.IsOpen = true;
        offering.UsesEvaluationWorkflow = true;
        offering.EvaluationState = state;
        offering.Applications.Add(CreateApplication("10000000146", ApplicationStatus.Pending));
        await db.SaveChangesAsync();
        var originalQuota = offering.Quota;
        var originalStatus = Assert.Single(offering.Applications).CurrentStatus;

        var result = await CreateService(db).CloseInvalidForRemediationAsync(
            offering.ProgramOfferingId,
            41,
            new ProgramOfferingRemediationCloseDto
            {
                RowVersion = Convert.ToBase64String(offering.RowVersion)
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(offering.IsOpen);
        Assert.Equal(state, offering.EvaluationState);
        Assert.Equal(originalQuota, offering.Quota);
        Assert.Equal(originalStatus, Assert.Single(offering.Applications).CurrentStatus);
        Assert.Equal(OfferingDocumentConfigurationHealth.ClosedWorkflow, result.Value!.DocumentConfigurationHealth);
        var audit = Assert.Single(
            db.SecurityAuditLogs,
            item => item.EventType == "ProgramOfferingClosedForRemediation");
        Assert.Equal(41, audit.ActorAdminId);
    }

    [Fact]
    public async Task Remediation_close_rejects_legacy_and_already_valid_open_offerings_without_audit()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var legacy = AddOffering(db, program.ProgramId);
        legacy.AcademicYearStart = 0;
        legacy.Term = AcademicTerm.LegacyUnspecified;
        legacy.IsOpen = true;
        var healthy = AddOffering(db, program.ProgramId);
        healthy.AcademicYearStart = 2027;
        healthy.IsOpen = true;
        healthy.DocumentRequirements.Add(DocumentRequirement(isRequired: true, isActive: true));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var legacyResult = await service.CloseInvalidForRemediationAsync(
            legacy.ProgramOfferingId,
            1,
            new ProgramOfferingRemediationCloseDto
            {
                RowVersion = Convert.ToBase64String(legacy.RowVersion)
            },
            CancellationToken.None);
        var healthyResult = await service.CloseInvalidForRemediationAsync(
            healthy.ProgramOfferingId,
            1,
            new ProgramOfferingRemediationCloseDto
            {
                RowVersion = Convert.ToBase64String(healthy.RowVersion)
            },
            CancellationToken.None);

        Assert.False(legacyResult.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, legacyResult.StatusCode);
        Assert.False(healthyResult.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, healthyResult.StatusCode);
        Assert.True(legacy.IsOpen);
        Assert.True(healthy.IsOpen);
        Assert.DoesNotContain(
            db.SecurityAuditLogs,
            item => item.EventType == "ProgramOfferingClosedForRemediation");
    }

    [Theory]
    [InlineData(OfferingEvaluationState.Finalized)]
    [InlineData(OfferingEvaluationState.Published)]
    public async Task Finalized_or_published_evaluation_offering_cannot_be_edited(OfferingEvaluationState state)
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var offering = AddOffering(db, program.ProgramId);
        offering.UsesEvaluationWorkflow = true;
        offering.EvaluationState = state;
        await db.SaveChangesAsync();

        var result = await CreateService(db).UpdateAsync(
            offering.ProgramOfferingId,
            1,
            UpdateRequest(
                offering,
                isOpen: false,
                quota: offering.Quota + 1,
                applicationDeadlineUtc: offering.ApplicationDeadlineUtc!.Value.AddDays(1)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    [Fact]
    public async Task Existing_legacy_offering_can_enable_evaluation_only_before_any_draft_exists()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var available = AddOffering(db, program.ProgramId);
        var locked = AddOffering(db, program.ProgramId);
        locked.Term = AcademicTerm.Spring;
        locked.Applications.Add(new Application
        {
            PublicId = Guid.NewGuid(),
            Tc = "10000000146",
            CurrentStatus = ApplicationStatus.Draft.ToString(),
            UsesDocumentWorkflow = true
        });
        await db.SaveChangesAsync();

        var enabled = await CreateService(db).UpdateAsync(
            available.ProgramOfferingId,
            1,
            UpdateRequest(available, isOpen: false, usesEvaluationWorkflow: true),
            CancellationToken.None);
        var blocked = await CreateService(db).UpdateAsync(
            locked.ProgramOfferingId,
            1,
            UpdateRequest(locked, isOpen: false, usesEvaluationWorkflow: true),
            CancellationToken.None);

        Assert.True(enabled.IsSuccess);
        Assert.True(available.UsesEvaluationWorkflow);
        Assert.False(blocked.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, blocked.StatusCode);
        Assert.False(locked.UsesEvaluationWorkflow);
    }

    [Fact]
    public async Task Existing_legacy_offering_cannot_enable_evaluation_and_open_without_a_valid_policy()
    {
        await using var db = TestDb.Create();
        var program = await SeedProgramAsync(db);
        var offering = AddOffering(db, program.ProgramId);
        offering.DocumentRequirements.Add(DocumentRequirement(isRequired: true, isActive: true));
        await db.SaveChangesAsync();

        var result = await CreateService(db).UpdateAsync(
            offering.ProgramOfferingId,
            1,
            UpdateRequest(offering, isOpen: true, usesEvaluationWorkflow: true),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.False(offering.UsesEvaluationWorkflow);
        Assert.False(offering.IsOpen);
    }

    private static ProgramOfferingService CreateService(GraduateAppDbContext db) =>
        new(db, new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)));

    private static async Task<GraduateApp.API.Models.Program> SeedProgramAsync(GraduateAppDbContext db)
    {
        var program = new GraduateApp.API.Models.Program
        {
            ProgramName = "Bilgisayar Mühendisliği",
            DegreeType = "Tezli Yüksek Lisans",
            IsActive = true,
            Institute = new Institute { InstituteName = "Fen Bilimleri" }
        };
        db.Programs.Add(program);
        await db.SaveChangesAsync();
        return program;
    }

    private static ProgramOfferingCreateDto CreateRequest(
        int programId,
        AcademicTerm term,
        int year = 2026,
        bool isOpen = false) => new()
        {
            ProgramId = programId,
            AcademicYearStart = year,
            Term = term,
            ApplicationStartUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            ApplicationDeadlineUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Quota = 10,
            IsOpen = isOpen
        };

    private static ProgramOffering AddOffering(GraduateAppDbContext db, int programId)
    {
        var offering = new ProgramOffering
        {
            ProgramId = programId,
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall,
            ApplicationStartUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            ApplicationDeadlineUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Quota = 10,
            IsOpen = false
        };
        db.ProgramOfferings.Add(offering);
        return offering;
    }

    private static ProgramOfferingDocumentRequirement DocumentRequirement(bool isRequired, bool isActive) => new()
    {
        PublicId = Guid.NewGuid(),
        DocumentCode = "TRANSCRIPT",
        NormalizedDocumentCode = "TRANSCRIPT",
        DisplayName = "Transkript",
        IsRequired = isRequired,
        IsActive = isActive,
        AllowedContentCategory = DocumentContentCategory.PdfOnly,
        MaximumBytes = 1024,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Application CreateApplication(string tc, ApplicationStatus status) => new()
    {
        PublicId = Guid.NewGuid(),
        Tc = tc,
        ApplicationDate = new DateTime(2026, 7, 17, 9, 0, 0, DateTimeKind.Utc),
        CurrentStatus = status.ToString(),
        UsesDocumentWorkflow = true
    };

    private static (Exam Ales, Exam Yds) AddValidExamEvaluationPolicy(
        GraduateAppDbContext db,
        ProgramOffering offering)
    {
        offering.UsesEvaluationWorkflow = true;
        var ales = new Exam { ExamName = "ALES" };
        var yds = new Exam { ExamName = "YDS" };
        db.Exams.Add(yds);
        offering.ExamRequirements.Add(new ProgramOfferingExamRequirement
        {
            Exam = ales,
            MinimumScore = 0m,
            IsRequired = true
        });
        offering.EvaluationCriteria.Add(new ProgramOfferingEvaluationCriterion
        {
            PublicId = Guid.NewGuid(),
            Code = "ALES",
            NormalizedCode = "ALES",
            DisplayName = "Kontrolsüz kullanıcı girdisi",
            SourceType = EvaluationCriterionSourceType.ExamScore,
            Exam = ales,
            WeightBasisPoints = 10000,
            MaximumRawScore = 100m,
            TieBreakPriority = 1
        });
        return (ales, yds);
    }

    private static ProgramOfferingRequirementInputDto Requirement(int examId, bool isRequired) => new()
    {
        ExamId = examId,
        MinimumScore = 0m,
        IsRequired = isRequired
    };

    private static ProgramOfferingUpdateDto UpdateRequest(
        ProgramOffering offering,
        bool isOpen,
        bool? usesEvaluationWorkflow = null,
        int? quota = null,
        DateTime? applicationDeadlineUtc = null,
        IReadOnlyList<ProgramOfferingRequirementInputDto>? examRequirements = null) => new()
        {
            ProgramId = offering.ProgramId,
            AcademicYearStart = offering.AcademicYearStart,
            Term = offering.Term,
            ApplicationStartUtc = offering.ApplicationStartUtc!.Value,
            ApplicationDeadlineUtc = applicationDeadlineUtc ?? offering.ApplicationDeadlineUtc!.Value,
            Quota = quota ?? offering.Quota,
            IsOpen = isOpen,
            UsesEvaluationWorkflow = usesEvaluationWorkflow ?? offering.UsesEvaluationWorkflow,
            ExamRequirements = examRequirements
                ?? offering.ExamRequirements
                    .Select(item => Requirement(item.ExamId, item.IsRequired))
                    .ToArray(),
            RowVersion = Convert.ToBase64String(offering.RowVersion)
        };

    public enum OrphanedExamRequirement
    {
        OptionalMatchingExam,
        MissingMatchingExam,
        DifferentRequiredExam
    }

    private sealed class SwitchableConcurrencyInterceptor : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Enabled
                ? ValueTask.FromException<InterceptionResult<int>>(new DbUpdateConcurrencyException())
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
