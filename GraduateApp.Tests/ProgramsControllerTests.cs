using GraduateApp.API.Controllers;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Tests;

public sealed class ProgramsControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Evaluation_offering_remains_open_when_submitted_applications_reach_quota()
    {
        await using var db = TestDb.Create();
        var offering = await SeedFullOfferingAsync(db, usesEvaluationWorkflow: true);

        var programs = await GetOpenProgramsAsync(db);

        Assert.Contains(programs, item => item.ProgramOfferingId == offering.ProgramOfferingId);
    }

    [Fact]
    public async Task Legacy_offering_is_hidden_when_submitted_applications_reach_quota()
    {
        await using var db = TestDb.Create();
        var offering = await SeedFullOfferingAsync(db, usesEvaluationWorkflow: false);

        var programs = await GetOpenProgramsAsync(db);

        Assert.DoesNotContain(programs, item => item.ProgramOfferingId == offering.ProgramOfferingId);
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("archived")]
    [InlineData("not-started")]
    [InlineData("expired")]
    [InlineData("inactive-program")]
    [InlineData("inactive-institute")]
    [InlineData("non-positive-quota")]
    public async Task Evaluation_quota_exemption_does_not_bypass_other_open_program_filters(string unavailableReason)
    {
        await using var db = TestDb.Create();
        var offering = await SeedFullOfferingAsync(db, usesEvaluationWorkflow: true);
        switch (unavailableReason)
        {
            case "closed":
                offering.IsOpen = false;
                break;
            case "archived":
                offering.IsArchived = true;
                break;
            case "not-started":
                offering.ApplicationStartUtc = Now.UtcDateTime.AddMinutes(1);
                break;
            case "expired":
                offering.ApplicationDeadlineUtc = Now.UtcDateTime.AddMinutes(-1);
                break;
            case "inactive-program":
                offering.Program.IsActive = false;
                break;
            case "inactive-institute":
                offering.Program.Institute.IsActive = false;
                break;
            case "non-positive-quota":
                offering.Quota = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(unavailableReason));
        }

        await db.SaveChangesAsync();

        var programs = await GetOpenProgramsAsync(db);

        Assert.DoesNotContain(programs, item => item.ProgramOfferingId == offering.ProgramOfferingId);
    }

    private static async Task<IReadOnlyList<OpenProgramDto>> GetOpenProgramsAsync(GraduateAppDbContext db)
    {
        var controller = new ProgramsController(db, new TestTimeProvider(Now));
        var actionResult = await controller.GetOpen(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        return Assert.IsAssignableFrom<IReadOnlyList<OpenProgramDto>>(ok.Value);
    }

    private static async Task<ProgramOffering> SeedFullOfferingAsync(
        GraduateAppDbContext db,
        bool usesEvaluationWorkflow)
    {
        var student = new Student
        {
            Tc = "10000000146",
            PublicId = Guid.NewGuid(),
            StudentName = "Test",
            StudentSurname = "Öğrenci",
            Email = "student@example.test",
            NormalizedEmail = "STUDENT@EXAMPLE.TEST",
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true
        };
        var offering = new ProgramOffering
        {
            Program = new GraduateApp.API.Models.Program
            {
                ProgramName = "Bilgisayar Mühendisliği",
                DegreeType = "Tezli Yüksek Lisans",
                IsActive = true,
                Institute = new Institute
                {
                    InstituteName = "Fen Bilimleri",
                    IsActive = true
                }
            },
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall,
            ApplicationStartUtc = Now.UtcDateTime.AddDays(-1),
            ApplicationDeadlineUtc = Now.UtcDateTime.AddDays(1),
            Quota = 1,
            IsOpen = true,
            UsesEvaluationWorkflow = usesEvaluationWorkflow
        };
        offering.Applications.Add(new Application
        {
            PublicId = Guid.NewGuid(),
            Tc = student.Tc,
            TcNavigation = student,
            ApplicationDate = Now.UtcDateTime,
            CurrentStatus = ApplicationStatus.Pending.ToString(),
            UsesDocumentWorkflow = true,
            UsesEvaluationWorkflow = usesEvaluationWorkflow
        });
        db.ProgramOfferings.Add(offering);
        await db.SaveChangesAsync();
        return offering;
    }
}
