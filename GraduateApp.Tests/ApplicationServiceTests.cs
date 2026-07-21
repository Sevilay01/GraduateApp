using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;

namespace GraduateApp.Tests;

public sealed class ApplicationServiceTests
{
    [Fact]
    public async Task Create_RejectsDuplicateOfferingApplication_ButAllowsNextTerm()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db, isOpen: true);
        var nextTerm = new ProgramOffering
        {
            Program = offering.Program,
            AcademicYearStart = offering.AcademicYearStart,
            Term = AcademicTerm.Spring,
            ApplicationStartUtc = offering.ApplicationStartUtc,
            ApplicationDeadlineUtc = offering.ApplicationDeadlineUtc,
            Quota = 10,
            IsOpen = true
        };
        db.ProgramOfferings.Add(nextTerm);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var first = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        var duplicate = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        var nextTermResult = await service.CreateAsync("10000000146", nextTerm.ProgramOfferingId, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, duplicate.StatusCode);
        Assert.True(nextTermResult.IsSuccess);
    }

    [Theory]
    [InlineData(false, "2026-07-01T00:00:00Z", "2026-08-01T00:00:00Z")]
    [InlineData(true, "2026-07-18T00:00:00Z", "2026-08-01T00:00:00Z")]
    [InlineData(true, "2026-07-01T00:00:00Z", "2026-07-16T00:00:00Z")]
    public async Task Create_RejectsClosed_NotStarted_OrExpiredOffering(
        bool isOpen,
        string start,
        string deadline)
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db, isOpen);
        offering.ApplicationStartUtc = DateTime.Parse(start, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        offering.ApplicationDeadlineUtc = DateTime.Parse(deadline, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(db.Applications);
    }

    [Fact]
    public async Task Create_rejects_offering_when_its_institute_is_inactive()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db, isOpen: true);
        offering.Program.Institute.IsActive = false;
        await db.SaveChangesAsync();

        var result = await CreateService(db).CreateAsync(
            "10000000146",
            offering.ProgramOfferingId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Empty(db.Applications);
    }

    [Fact]
    public async Task Deactivation_preserves_historical_application_and_program_name()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db, isOpen: true);
        var created = await CreateService(db).CreateAsync(
            "10000000146",
            offering.ProgramOfferingId,
            CancellationToken.None);
        Assert.True(created.IsSuccess);
        var rowVersion = Convert.ToBase64String(offering.Program.RowVersion);

        var programResult = await new ProgramAdminService(
            db,
            new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero))).SetActiveAsync(
            offering.ProgramId,
            1,
            false,
            new CatalogConcurrencyDto { RowVersion = rowVersion },
            CancellationToken.None);
        var instituteResult = await new InstituteAdminService(
            db,
            new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 10, 1, 0, TimeSpan.Zero))).SetActiveAsync(
            offering.Program.InstituteId,
            1,
            false,
            new CatalogConcurrencyDto { RowVersion = Convert.ToBase64String(offering.Program.Institute.RowVersion) },
            CancellationToken.None);
        var historical = await CreateService(db).GetForStudentAsync("10000000146", CancellationToken.None);

        Assert.True(programResult.IsSuccess);
        Assert.True(instituteResult.IsSuccess);
        Assert.Single(db.Applications);
        Assert.Single(historical);
        Assert.Equal("Bilgisayar Mühendisliği", historical[0].ProgramName);
    }

    [Fact]
    public async Task Create_ValidatesRequiredScore_AndCapturesImmutableSnapshot()
    {
        await using var db = TestDb.Create();
        var exam = new Exam { ExamName = "ALES" };
        var offering = await SeedAsync(db, isOpen: true);
        offering.ExamRequirements.Add(new ProgramOfferingExamRequirement
        {
            Exam = exam,
            MinimumScore = 70,
            MinimumValidityDate = new DateOnly(2025, 1, 1),
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

        var result = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        var score = db.StudentExamScores.Single();
        score.Score = 90;
        await db.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(db.ApplicationScoreSnapshots);
        Assert.Equal(75, snapshot.ScoreSnapshot);
        Assert.Equal("ALES", snapshot.ExamNameSnapshot);
    }

    [Fact]
    public async Task Create_explains_missing_required_exam_score()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db, isOpen: true);
        offering.ExamRequirements.Add(new ProgramOfferingExamRequirement
        {
            Exam = new Exam { ExamName = "ALES" },
            MinimumScore = 70,
            IsRequired = true
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).CreateAsync(
            "10000000146",
            offering.ProgramOfferingId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("ALES sınav sonucunuz bulunmuyor", result.Error, StringComparison.Ordinal);
        Assert.Contains("Sınav sonuçlarım", result.Error, StringComparison.Ordinal);
        Assert.Empty(db.Applications);
    }

    [Theory]
    [InlineData(69, "2026-01-01")]
    [InlineData(75, "2024-12-31")]
    public async Task Create_RejectsLowOrExpiredRequiredScore(decimal scoreValue, string examDate)
    {
        await using var db = TestDb.Create();
        var exam = new Exam { ExamName = "ALES" };
        var offering = await SeedAsync(db, isOpen: true);
        offering.ExamRequirements.Add(new ProgramOfferingExamRequirement
        {
            Exam = exam,
            MinimumScore = 70,
            MinimumValidityDate = new DateOnly(2025, 1, 1),
            IsRequired = true
        });
        db.StudentExamScores.Add(new StudentExamScore
        {
            Tc = "10000000146",
            Exam = exam,
            Score = scoreValue,
            ExamDate = DateOnly.Parse(examDate)
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).CreateAsync(
            "10000000146",
            offering.ProgramOfferingId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            scoreValue < 70 ? "puanınız yetersiz" : "sınav tarihiniz ilan koşulunu sağlamıyor",
            result.Error,
            StringComparison.Ordinal);
        Assert.Empty(db.Applications);
        Assert.Empty(db.ApplicationScoreSnapshots);
    }

    [Fact]
    public async Task StudentList_ReturnsOnlyAuthenticatedStudentsApplications()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db, isOpen: true);
        db.Students.Add(CreateStudent("10000000154", "other@example.test"));
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        await service.CreateAsync("10000000154", offering.ProgramOfferingId, CancellationToken.None);

        var mine = await service.GetForStudentAsync("10000000146", CancellationToken.None);

        Assert.Single(mine);
        Assert.Equal("10000000146", db.Applications.Single(item => item.ApplicationId == mine[0].ApplicationId).Tc);
    }

    [Fact]
    public async Task Create_rejects_application_when_quota_is_full()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db, isOpen: true);
        offering.Quota = 1;
        db.Students.Add(CreateStudent("10000000154", "other@example.test"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var first = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        var second = await service.CreateAsync("10000000154", offering.ProgramOfferingId, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, second.StatusCode);
        Assert.Single(db.Applications);
    }

    [Fact]
    public async Task Admin_detail_returns_masked_tc_by_default()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db, isOpen: true);
        var service = CreateService(db);
        var created = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);

        var detail = await service.GetDetailForAdminAsync(created.Value!.ApplicationId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("*******0146", detail!.MaskedTc);
        Assert.DoesNotContain("10000000146", detail.MaskedTc, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateStatus_PendingToUnderReview_adds_exactly_one_history_and_one_audit()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db, isOpen: true);
        var service = CreateService(db);
        var created = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        var historyCountBefore = db.ApplicationStatusHistories.Count();

        var result = await service.UpdateStatusAsync(
            created.Value!.ApplicationId,
            adminId: 1,
            new ApplicationStatusUpdateDto
            {
                NewStatus = ApplicationStatus.UnderReview,
                RowVersion = created.Value.RowVersion
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationStatus.UnderReview.ToString(), db.Applications.Single().CurrentStatus);
        Assert.Equal(historyCountBefore + 1, db.ApplicationStatusHistories.Count());
        var history = db.ApplicationStatusHistories.Single(item => item.PreviousStatus == ApplicationStatus.Pending.ToString());
        Assert.Equal(ApplicationStatus.UnderReview.ToString(), history.StatusName);
        var audit = Assert.Single(db.SecurityAuditLogs);
        Assert.Equal("ApplicationStatusChanged", audit.EventType);
        Assert.Equal(created.Value.ApplicationId.ToString(), audit.TargetId);
    }

    [Theory]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Rejected)]
    public async Task UpdateStatus_UnderReview_can_transition_to_final_status(ApplicationStatus finalStatus)
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db, isOpen: true);
        var service = CreateService(db);
        var created = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);
        var underReview = await service.UpdateStatusAsync(
            created.Value!.ApplicationId,
            adminId: 1,
            new ApplicationStatusUpdateDto
            {
                NewStatus = ApplicationStatus.UnderReview,
                RowVersion = created.Value.RowVersion
            },
            CancellationToken.None);
        var currentRowVersion = Convert.ToBase64String(db.Applications.Single().RowVersion);

        var result = await service.UpdateStatusAsync(
            created.Value.ApplicationId,
            adminId: 1,
            new ApplicationStatusUpdateDto
            {
                NewStatus = finalStatus,
                RowVersion = currentRowVersion
            },
            CancellationToken.None);

        Assert.True(underReview.IsSuccess);
        Assert.True(result.IsSuccess);
        Assert.Equal(finalStatus.ToString(), db.Applications.Single().CurrentStatus);
        Assert.Equal(3, db.ApplicationStatusHistories.Count());
        Assert.Equal(2, db.SecurityAuditLogs.Count());
    }

    [Fact]
    public async Task UpdateStatus_RejectsInvalidTransition()
    {
        await using var db = TestDb.Create();
        var offering = await SeedAsync(db, isOpen: true);
        var service = CreateService(db);
        var created = await service.CreateAsync("10000000146", offering.ProgramOfferingId, CancellationToken.None);

        var result = await service.UpdateStatusAsync(
            created.Value!.ApplicationId,
            adminId: 1,
            new ApplicationStatusUpdateDto
            {
                NewStatus = ApplicationStatus.Approved,
                RowVersion = created.Value.RowVersion
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(ApplicationStatus.Pending.ToString(), db.Applications.Single().CurrentStatus);
    }

    private static ApplicationService CreateService(GraduateAppDbContext db) =>
        new(db, new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)));

    private static async Task<ProgramOffering> SeedAsync(GraduateAppDbContext db, bool isOpen)
    {
        var program = new GraduateApp.API.Models.Program
        {
            ProgramName = "Bilgisayar Mühendisliği",
            DegreeType = "Tezli Yüksek Lisans",
            IsActive = true,
            RowVersion = [1, 2, 3, 4, 5, 6, 7, 8],
            Institute = new Institute
            {
                InstituteName = "Fen Bilimleri",
                RowVersion = [8, 7, 6, 5, 4, 3, 2, 1]
            }
        };
        var offering = new ProgramOffering
        {
            Program = program,
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall,
            ApplicationStartUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            ApplicationDeadlineUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Quota = 10,
            IsOpen = isOpen
        };
        db.Students.Add(CreateStudent("10000000146", "student@example.test"));
        db.ProgramOfferings.Add(offering);
        await db.SaveChangesAsync();
        return offering;
    }

    private static Student CreateStudent(string tc, string email) => new()
    {
        Tc = tc,
        StudentName = "Test",
        StudentSurname = "Öğrenci",
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        PasswordHash = "hash",
        SecurityStamp = Guid.NewGuid().ToString("N")
    };
}
