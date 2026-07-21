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
        int year = 2026) => new()
        {
            ProgramId = programId,
            AcademicYearStart = year,
            Term = term,
            ApplicationStartUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            ApplicationDeadlineUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Quota = 10,
            IsOpen = false
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
