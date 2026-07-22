using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GraduateApp.Tests;

public sealed class EvaluationPolicyServiceTests
{
    [Fact]
    public async Task Create_persists_normalized_audited_manual_criterion()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);

        var result = await Service(db).CreateAsync(
            offering.ProgramOfferingId,
            7,
            Criterion("  interview  ", EvaluationCriterionSourceType.ManualScore, 10000, 100m, 1),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(db.ProgramOfferingEvaluationCriteria);
        Assert.Equal("INTERVIEW", stored.NormalizedCode);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "EvaluationCriterionCreated");
    }

    [Theory]
    [InlineData(EvaluationCriterionSourceType.UndergraduateGpa, 100, null)]
    [InlineData(EvaluationCriterionSourceType.ExamScore, 100, null)]
    [InlineData(EvaluationCriterionSourceType.ManualScore, 4, null)]
    public async Task Create_rejects_invalid_source_configuration(
        EvaluationCriterionSourceType sourceType,
        int maximum,
        int? examId)
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);

        var result = await Service(db).CreateAsync(
            offering.ProgramOfferingId,
            7,
            Criterion("criterion", sourceType, 10000, maximum, 1, examId),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Empty(db.ProgramOfferingEvaluationCriteria);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task Exam_criterion_must_reference_required_offering_exam()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db, examIsRequired: false);
        var examId = offering.ExamRequirements.Single().ExamId;

        var result = await Service(db).CreateAsync(
            offering.ProgramOfferingId,
            7,
            Criterion("ales", EvaluationCriterionSourceType.ExamScore, 10000, 100m, 1, examId),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    [Fact]
    public async Task Weight_total_cannot_exceed_ten_thousand_basis_points()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        var service = Service(db);
        Assert.True((await service.CreateAsync(
            offering.ProgramOfferingId,
            7,
            Criterion("gpa", EvaluationCriterionSourceType.UndergraduateGpa, 6000, 4m, 1),
            CancellationToken.None)).IsSuccess);

        var result = await service.CreateAsync(
            offering.ProgramOfferingId,
            7,
            Criterion("manual", EvaluationCriterionSourceType.ManualScore, 5000, 100m, 2),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Single(db.ProgramOfferingEvaluationCriteria);
    }

    [Theory]
    [InlineData("GPA", 2)]
    [InlineData("OTHER", 1)]
    public async Task Code_and_tie_break_priority_must_be_unique(string secondCode, int secondPriority)
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        var service = Service(db);
        Assert.True((await service.CreateAsync(
            offering.ProgramOfferingId,
            7,
            Criterion("gpa", EvaluationCriterionSourceType.UndergraduateGpa, 5000, 4m, 1),
            CancellationToken.None)).IsSuccess);

        var result = await service.CreateAsync(
            offering.ProgramOfferingId,
            7,
            Criterion(secondCode, EvaluationCriterionSourceType.ManualScore, 5000, 100m, secondPriority),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    [Fact]
    public async Task Policy_is_locked_after_even_a_draft_application_exists()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        offering.Applications.Add(new Application
        {
            PublicId = Guid.NewGuid(),
            Tc = "10000000146",
            CurrentStatus = ApplicationStatus.Draft.ToString(),
            UsesDocumentWorkflow = true,
            UsesEvaluationWorkflow = true
        });
        await db.SaveChangesAsync();

        var result = await Service(db).CreateAsync(
            offering.ProgramOfferingId,
            7,
            Criterion("manual", EvaluationCriterionSourceType.ManualScore, 10000, 100m, 1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Theory]
    [InlineData(OfferingEvaluationState.Finalized)]
    [InlineData(OfferingEvaluationState.Published)]
    public async Task Policy_is_locked_after_finalization(OfferingEvaluationState state)
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        offering.EvaluationState = state;
        await db.SaveChangesAsync();

        var result = await Service(db).CreateAsync(
            offering.ProgramOfferingId,
            7,
            Criterion("manual", EvaluationCriterionSourceType.ManualScore, 10000, 100m, 1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    [Fact]
    public async Task Legacy_offering_cannot_receive_evaluation_policy()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db);
        offering.UsesEvaluationWorkflow = false;
        await db.SaveChangesAsync();

        var result = await Service(db).CreateAsync(
            offering.ProgramOfferingId,
            7,
            Criterion("manual", EvaluationCriterionSourceType.ManualScore, 10000, 100m, 1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
    }

    [Fact]
    public async Task Stale_policy_row_version_returns_safe_conflict_without_persisted_audit()
    {
        var interceptor = new ToggleConcurrencyInterceptor();
        await using var db = TestDb.Create(interceptor);
        var offering = await SeedAsync(db);
        var criterion = new ProgramOfferingEvaluationCriterion
        {
            PublicId = Guid.NewGuid(),
            Code = "MANUAL",
            NormalizedCode = "MANUAL",
            DisplayName = "Mülakat",
            SourceType = EvaluationCriterionSourceType.ManualScore,
            WeightBasisPoints = 10000,
            MaximumRawScore = 100m,
            TieBreakPriority = 1
        };
        offering.EvaluationCriteria.Add(criterion);
        await db.SaveChangesAsync();
        interceptor.Enabled = true;

        var result = await Service(db).UpdateAsync(
            offering.ProgramOfferingId,
            criterion.PublicId,
            7,
            new EvaluationCriterionUpdateDto
            {
                Code = criterion.Code,
                DisplayName = "Yeni ad",
                SourceType = criterion.SourceType,
                WeightBasisPoints = criterion.WeightBasisPoints,
                MaximumRawScore = criterion.MaximumRawScore,
                TieBreakPriority = criterion.TieBreakPriority,
                RowVersion = Convert.ToBase64String(criterion.RowVersion)
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.DoesNotContain("RowVersion", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(db.SecurityAuditLogs, item => item.EventType == "EvaluationCriterionUpdated");
    }

    private static EvaluationPolicyService Service(GraduateAppDbContext db) =>
        new(db, new TestTimeProvider(new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)));

    private static EvaluationCriterionCreateDto Criterion(
        string code,
        EvaluationCriterionSourceType sourceType,
        int weight,
        decimal maximum,
        int priority,
        int? examId = null) => new()
        {
            Code = code,
            DisplayName = code.Trim(),
            SourceType = sourceType,
            ExamId = examId,
            WeightBasisPoints = weight,
            MaximumRawScore = maximum,
            TieBreakPriority = priority
        };

    private static async Task<ProgramOffering> SeedAsync(GraduateAppDbContext db, bool examIsRequired = true)
    {
        var exam = new Exam { ExamName = "ALES" };
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
            Quota = 2,
            UsesEvaluationWorkflow = true,
            EvaluationState = OfferingEvaluationState.Configuring,
            ExamRequirements =
            [
                new ProgramOfferingExamRequirement
                {
                    Exam = exam,
                    MinimumScore = 0m,
                    IsRequired = examIsRequired
                }
            ]
        };
        db.ProgramOfferings.Add(offering);
        await db.SaveChangesAsync();
        return offering;
    }

    private sealed class ToggleConcurrencyInterceptor : SaveChangesInterceptor
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
