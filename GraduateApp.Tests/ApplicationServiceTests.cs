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
            DegreeType = "Tezli",
            IsActive = true,
            Institute = new Institute { InstituteName = "Fen Bilimleri" }
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
