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

    private static ProgramOfferingUpdateDto UpdateRequest(
        ProgramOffering offering,
        bool isOpen,
        bool? usesEvaluationWorkflow = null,
        int? quota = null,
        DateTime? applicationDeadlineUtc = null) => new()
        {
            ProgramId = offering.ProgramId,
            AcademicYearStart = offering.AcademicYearStart,
            Term = offering.Term,
            ApplicationStartUtc = offering.ApplicationStartUtc!.Value,
            ApplicationDeadlineUtc = applicationDeadlineUtc ?? offering.ApplicationDeadlineUtc!.Value,
            Quota = quota ?? offering.Quota,
            IsOpen = isOpen,
            UsesEvaluationWorkflow = usesEvaluationWorkflow ?? offering.UsesEvaluationWorkflow,
            RowVersion = Convert.ToBase64String(offering.RowVersion)
        };

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
