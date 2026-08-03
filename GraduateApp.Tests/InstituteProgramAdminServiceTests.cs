using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GraduateApp.Tests;

public sealed class InstituteProgramAdminServiceTests
{
    private static readonly byte[] RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public void Degree_types_match_the_canonical_values_observed_in_the_database()
    {
        Assert.Equal(
            [
                "Doktora",
                "Tezli Yüksek Lisans",
                "Tezsiz Yüksek Lisans",
                "Uzaktan Tezsiz Yüksek Lisans"
            ],
            DegreeTypeCatalog.Values);
        Assert.True(DegreeTypeCatalog.TryCanonicalize("  tezli yüksek lisans ", out var canonical));
        Assert.Equal("Tezli Yüksek Lisans", canonical);
        Assert.False(DegreeTypeCatalog.TryCanonicalize("Lisans", out _));
    }

    [Fact]
    public async Task Institute_create_trims_name_rejects_duplicates_and_writes_audit()
    {
        await using var db = TestDb.Create();
        var service = CreateInstituteService(db);

        var created = await service.CreateAsync(
            42,
            new InstituteCreateDto { InstituteName = "  Fen Bilimleri  " },
            CancellationToken.None);
        var duplicate = await service.CreateAsync(
            42,
            new InstituteCreateDto { InstituteName = "Fen Bilimleri" },
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal("Fen Bilimleri", db.Institutes.Single().InstituteName);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, duplicate.StatusCode);
        var audit = Assert.Single(db.SecurityAuditLogs);
        Assert.Equal("InstituteCreated", audit.EventType);
        Assert.Equal(42, audit.ActorAdminId);
    }

    [Fact]
    public async Task Institute_update_trims_name_and_preserves_active_state()
    {
        await using var db = TestDb.Create();
        var institute = new Institute
        {
            InstituteName = "Fen Bilimleri",
            IsActive = false,
            RowVersion = RowVersion
        };
        db.Institutes.Add(institute);
        await db.SaveChangesAsync();

        var updated = await CreateInstituteService(db).UpdateAsync(
            institute.InstituteId,
            4,
            new InstituteUpdateDto
            {
                InstituteName = "  Fen ve Mühendislik Bilimleri  ",
                RowVersion = Convert.ToBase64String(RowVersion)
            },
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Equal("Fen ve Mühendislik Bilimleri", updated.Value!.InstituteName);
        Assert.False(updated.Value.IsActive);
        Assert.Equal("InstituteUpdated", db.SecurityAuditLogs.Single().EventType);
    }

    [Fact]
    public async Task Program_create_canonicalizes_degree_and_rejects_duplicate_composite_key()
    {
        await using var db = TestDb.Create();
        var institute = new Institute { InstituteName = "Fen Bilimleri", RowVersion = RowVersion };
        db.Institutes.Add(institute);
        await db.SaveChangesAsync();
        var service = CreateProgramService(db);
        var request = new ProgramCreateDto
        {
            InstituteId = institute.InstituteId,
            ProgramName = "  Bilgisayar Mühendisliği  ",
            ProgramNameEnglish = "  Computer Engineering  ",
            DegreeType = "tezli yüksek lisans"
        };

        var created = await service.CreateAsync(9, request, CancellationToken.None);
        var duplicate = await service.CreateAsync(
            9,
            new ProgramCreateDto
            {
                InstituteId = institute.InstituteId,
                ProgramName = "Bilgisayar Mühendisliği",
                DegreeType = "Tezli Yüksek Lisans"
            },
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal("Bilgisayar Mühendisliği", created.Value!.ProgramName);
        Assert.Equal("Computer Engineering", created.Value.ProgramNameEnglish);
        Assert.Equal("Tezli Yüksek Lisans", created.Value.DegreeType);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, duplicate.StatusCode);
        Assert.Single(db.Programs);
        Assert.Equal("ProgramCreated", db.SecurityAuditLogs.Single().EventType);
    }

    [Fact]
    public async Task Program_validation_rejects_missing_institute_and_degree_but_allows_same_name_in_another_institute()
    {
        await using var db = TestDb.Create();
        var firstInstitute = new Institute { InstituteName = "Fen Bilimleri" };
        var secondInstitute = new Institute { InstituteName = "Sosyal Bilimler" };
        db.Institutes.AddRange(firstInstitute, secondInstitute);
        await db.SaveChangesAsync();
        var service = CreateProgramService(db);
        var first = await service.CreateAsync(
            1,
            new ProgramCreateDto
            {
                InstituteId = firstInstitute.InstituteId,
                ProgramName = "Ortak Program",
                DegreeType = "Doktora"
            },
            CancellationToken.None);
        var anotherInstitute = await service.CreateAsync(
            1,
            new ProgramCreateDto
            {
                InstituteId = secondInstitute.InstituteId,
                ProgramName = "Ortak Program",
                DegreeType = "Doktora"
            },
            CancellationToken.None);
        var missingInstitute = await service.CreateAsync(
            1,
            new ProgramCreateDto
            {
                InstituteId = int.MaxValue,
                ProgramName = "Fizik",
                DegreeType = "Doktora"
            },
            CancellationToken.None);
        var invalidDegree = await service.CreateAsync(
            1,
            new ProgramCreateDto
            {
                InstituteId = firstInstitute.InstituteId,
                ProgramName = "Fizik",
                DegreeType = "Lisans"
            },
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(anotherInstitute.IsSuccess);
        Assert.Equal(StatusCodes.Status400BadRequest, missingInstitute.StatusCode);
        Assert.Contains("enstitü bulunamadı", missingInstitute.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StatusCodes.Status400BadRequest, invalidDegree.StatusCode);
        Assert.Contains("Derece türü", invalidDegree.Error, StringComparison.Ordinal);
        Assert.Equal(2, db.Programs.Count());
    }

    [Fact]
    public async Task Lists_apply_filters_stable_order_paging_and_effective_active_state()
    {
        await using var db = TestDb.CreateWithStrictQueryWarnings();
        var active = new Institute { InstituteName = "Fen Bilimleri", IsActive = true, RowVersion = RowVersion };
        var inactive = new Institute { InstituteName = "Sosyal Bilimler", IsActive = false, RowVersion = RowVersion };
        db.Programs.AddRange(
            new GraduateApp.API.Models.Program
            {
                Institute = inactive,
                ProgramName = "Tarih",
                DegreeType = "Doktora",
                IsActive = true,
                RowVersion = RowVersion
            },
            new GraduateApp.API.Models.Program
            {
                Institute = active,
                ProgramName = "Kimya",
                DegreeType = "Doktora",
                IsActive = false,
                RowVersion = RowVersion
            },
            new GraduateApp.API.Models.Program
            {
                Institute = active,
                ProgramName = "Bilgisayar",
                DegreeType = "Tezli Yüksek Lisans",
                IsActive = true,
                RowVersion = RowVersion
            });
        await db.SaveChangesAsync();

        var institutes = await CreateInstituteService(db).GetAsync(null, null, 0, 1, CancellationToken.None);
        var programs = await CreateProgramService(db).GetAsync(null, true, null, null, 1, 10, CancellationToken.None);
        var filtered = await CreateProgramService(db).GetAsync(
            "Bilgi",
            true,
            active.InstituteId,
            "Tezli Yüksek Lisans",
            1,
            10,
            CancellationToken.None);

        Assert.Equal(1, institutes.Page);
        Assert.Equal(10, institutes.PageSize);
        Assert.Equal(["Fen Bilimleri", "Sosyal Bilimler"], institutes.Items.Select(item => item.InstituteName));
        Assert.Equal(["Bilgisayar", "Tarih"], programs.Items.Select(item => item.ProgramName));
        Assert.True(programs.Items[0].IsEffectivelyActive);
        Assert.False(programs.Items[1].IsEffectivelyActive);
        Assert.Equal("Bilgisayar", Assert.Single(filtered.Items).ProgramName);
    }

    [Fact]
    public async Task Physical_delete_is_blocked_when_dependents_exist()
    {
        await using var db = TestDb.Create();
        var institute = new Institute { InstituteName = "Fen Bilimleri", RowVersion = RowVersion };
        var program = new GraduateApp.API.Models.Program
        {
            Institute = institute,
            ProgramName = "Bilgisayar",
            DegreeType = "Doktora",
            RowVersion = RowVersion
        };
        program.Offerings.Add(new ProgramOffering
        {
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall,
            Quota = 1
        });
        db.Programs.Add(program);
        await db.SaveChangesAsync();
        var encoded = Convert.ToBase64String(RowVersion);

        var instituteDelete = await CreateInstituteService(db).DeleteAsync(
            institute.InstituteId,
            1,
            encoded,
            CancellationToken.None);
        var programDelete = await CreateProgramService(db).DeleteAsync(
            program.ProgramId,
            1,
            encoded,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, instituteDelete.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, programDelete.StatusCode);
        Assert.Single(db.Institutes);
        Assert.Single(db.Programs);
    }

    [Fact]
    public async Task Unused_program_then_institute_can_be_deleted_with_rowversion()
    {
        await using var db = TestDb.Create();
        var institute = new Institute
        {
            InstituteName = "Fen Bilimleri",
            RowVersion = RowVersion
        };
        var program = new GraduateApp.API.Models.Program
        {
            Institute = institute,
            ProgramName = "İstatistik",
            DegreeType = "Doktora",
            RowVersion = RowVersion
        };
        db.Programs.Add(program);
        await db.SaveChangesAsync();
        var encoded = Convert.ToBase64String(RowVersion);

        var programDelete = await CreateProgramService(db).DeleteAsync(
            program.ProgramId,
            3,
            encoded,
            CancellationToken.None);
        var instituteDelete = await CreateInstituteService(db).DeleteAsync(
            institute.InstituteId,
            3,
            encoded,
            CancellationToken.None);

        Assert.True(programDelete.IsSuccess);
        Assert.True(instituteDelete.IsSuccess);
        Assert.Empty(db.Programs);
        Assert.Empty(db.Institutes);
        Assert.Equal(
            ["ProgramDeleted", "InstituteDeleted"],
            db.SecurityAuditLogs.OrderBy(item => item.AuditId).Select(item => item.EventType));
    }

    [Fact]
    public async Task Stale_institute_update_returns_safe_conflict()
    {
        var interceptor = new SwitchableConcurrencyInterceptor();
        await using var db = TestDb.Create(interceptor);
        var institute = new Institute { InstituteName = "Fen Bilimleri", RowVersion = RowVersion };
        db.Institutes.Add(institute);
        await db.SaveChangesAsync();
        interceptor.Enabled = true;

        var result = await CreateInstituteService(db).UpdateAsync(
            institute.InstituteId,
            1,
            new InstituteUpdateDto
            {
                InstituteName = "Fen ve Mühendislik Bilimleri",
                RowVersion = Convert.ToBase64String(RowVersion)
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.DoesNotContain("RowVersion", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Institute_deactivate_then_activate_with_current_rowversion_succeeds()
    {
        await using var db = TestDb.Create();
        var institute = new Institute
        {
            InstituteName = "Fen Bilimleri",
            IsActive = true,
            RowVersion = RowVersion
        };
        db.Institutes.Add(institute);
        await db.SaveChangesAsync();
        var service = CreateInstituteService(db);

        var deactivated = await service.SetActiveAsync(
            institute.InstituteId,
            7,
            false,
            new CatalogConcurrencyDto { RowVersion = Convert.ToBase64String(RowVersion) },
            CancellationToken.None);
        var activated = await service.SetActiveAsync(
            institute.InstituteId,
            7,
            true,
            new CatalogConcurrencyDto { RowVersion = deactivated.Value!.RowVersion },
            CancellationToken.None);

        Assert.True(deactivated.IsSuccess);
        Assert.True(activated.IsSuccess);
        Assert.True(db.Institutes.Single().IsActive);
        Assert.Equal(
            ["InstituteDeactivated", "InstituteActivated"],
            db.SecurityAuditLogs.OrderBy(item => item.AuditId).Select(item => item.EventType));
    }

    [Fact]
    public async Task Stale_institute_toggle_returns_safe_conflict()
    {
        var interceptor = new SwitchableConcurrencyInterceptor();
        await using var db = TestDb.Create(interceptor);
        var institute = new Institute
        {
            InstituteName = "Fen Bilimleri",
            IsActive = false,
            RowVersion = RowVersion
        };
        db.Institutes.Add(institute);
        await db.SaveChangesAsync();
        interceptor.Enabled = true;

        var result = await CreateInstituteService(db).SetActiveAsync(
            institute.InstituteId,
            1,
            true,
            new CatalogConcurrencyDto { RowVersion = Convert.ToBase64String(RowVersion) },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Contains("başka bir kullanıcı", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RowVersion", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Program_activate_and_deactivate_require_rowversion_and_write_audit()
    {
        await using var db = TestDb.Create();
        var program = new GraduateApp.API.Models.Program
        {
            Institute = new Institute { InstituteName = "Fen Bilimleri" },
            ProgramName = "Fizik",
            DegreeType = "Doktora",
            IsActive = true,
            RowVersion = RowVersion
        };
        db.Programs.Add(program);
        await db.SaveChangesAsync();
        var service = CreateProgramService(db);

        var invalid = await service.SetActiveAsync(
            program.ProgramId,
            7,
            false,
            new CatalogConcurrencyDto { RowVersion = "invalid" },
            CancellationToken.None);
        var deactivated = await service.SetActiveAsync(
            program.ProgramId,
            7,
            false,
            new CatalogConcurrencyDto { RowVersion = Convert.ToBase64String(RowVersion) },
            CancellationToken.None);
        var activated = await service.SetActiveAsync(
            program.ProgramId,
            7,
            true,
            new CatalogConcurrencyDto { RowVersion = deactivated.Value!.RowVersion },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, invalid.StatusCode);
        Assert.True(deactivated.IsSuccess);
        Assert.True(activated.IsSuccess);
        Assert.True(db.Programs.Single().IsActive);
        Assert.Equal(
            ["ProgramDeactivated", "ProgramActivated"],
            db.SecurityAuditLogs.OrderBy(item => item.AuditId).Select(item => item.EventType));
    }

    [Fact]
    public async Task Stale_program_toggle_returns_safe_conflict()
    {
        var interceptor = new SwitchableConcurrencyInterceptor();
        await using var db = TestDb.Create(interceptor);
        var program = new GraduateApp.API.Models.Program
        {
            Institute = new Institute { InstituteName = "Fen Bilimleri" },
            ProgramName = "Fizik",
            DegreeType = "Doktora",
            IsActive = true,
            RowVersion = RowVersion
        };
        db.Programs.Add(program);
        await db.SaveChangesAsync();
        interceptor.Enabled = true;

        var result = await CreateProgramService(db).SetActiveAsync(
            program.ProgramId,
            1,
            false,
            new CatalogConcurrencyDto { RowVersion = Convert.ToBase64String(RowVersion) },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Contains("başka bir kullanıcı", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RowVersion", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static InstituteAdminService CreateInstituteService(GraduateAppDbContext db) =>
        new(db, new TestTimeProvider(new DateTimeOffset(2026, 7, 21, 9, 0, 0, TimeSpan.Zero)));

    private static ProgramAdminService CreateProgramService(GraduateAppDbContext db) =>
        new(db, new TestTimeProvider(new DateTimeOffset(2026, 7, 21, 9, 0, 0, TimeSpan.Zero)));

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
