using GraduateApp.API.Controllers;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.AspNetCore.Http;
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
    [InlineData(DocumentRequirementScenario.None, false)]
    [InlineData(DocumentRequirementScenario.ActiveOptional, false)]
    [InlineData(DocumentRequirementScenario.InactiveRequired, false)]
    [InlineData(DocumentRequirementScenario.ActiveRequired, true)]
    public async Task Workflow_offering_visibility_requires_an_active_required_document(
        DocumentRequirementScenario scenario,
        bool expectedVisible)
    {
        await using var db = TestDb.Create();
        var offering = await SeedFullOfferingAsync(
            db,
            usesEvaluationWorkflow: true,
            documentRequirementScenario: scenario,
            includeSubmittedApplication: false);

        var programs = await GetOpenProgramsAsync(db);

        Assert.Equal(
            expectedVisible,
            programs.Any(item => item.ProgramOfferingId == offering.ProgramOfferingId));
    }

    [Fact]
    public async Task Legacy_document_workflow_sentinel_preserves_existing_open_list_behavior()
    {
        await using var db = TestDb.Create();
        var offering = await SeedFullOfferingAsync(
            db,
            usesEvaluationWorkflow: true,
            documentRequirementScenario: DocumentRequirementScenario.None,
            includeSubmittedApplication: false);
        offering.AcademicYearStart = 0;
        offering.Term = AcademicTerm.LegacyUnspecified;
        await db.SaveChangesAsync();

        var programs = await GetOpenProgramsAsync(db);

        Assert.Contains(programs, item => item.ProgramOfferingId == offering.ProgramOfferingId);
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


    [Theory]
    [InlineData("İstatistik")]
    [InlineData("Sosyal Bilimler")]
    [InlineData("Doktora")]
    public async Task Open_program_search_matches_program_institute_and_degree_in_database_query(string search)
    {
        await using var db = TestDb.Create();
        var expected = await SeedFullOfferingAsync(
            db,
            usesEvaluationWorkflow: true,
            includeSubmittedApplication: false);
        expected.Program.ProgramName = "İstatistik";
        expected.Program.DegreeType = "Doktora";
        expected.Program.Institute.InstituteName = "Sosyal Bilimler";

        var other = await SeedFullOfferingAsync(
            db,
            usesEvaluationWorkflow: true,
            includeSubmittedApplication: false);
        other.Program.ProgramName = "Kimya";
        other.Program.DegreeType = "Tezli Yüksek Lisans";
        other.Program.Institute.InstituteName = "Fen Bilimleri";
        await db.SaveChangesAsync();

        var programs = await GetOpenProgramsAsync(db, new OpenProgramSearchQueryDto
        {
            Search = search
        });

        var match = Assert.Single(programs);
        Assert.Equal(expected.ProgramOfferingId, match.ProgramOfferingId);
    }

    [Fact]
    public async Task Open_program_search_combines_academic_year_and_term_filters()
    {
        await using var db = TestDb.Create();
        var fall = await SeedFullOfferingAsync(
            db,
            usesEvaluationWorkflow: true,
            includeSubmittedApplication: false);
        var spring = await SeedFullOfferingAsync(
            db,
            usesEvaluationWorkflow: true,
            includeSubmittedApplication: false);
        spring.AcademicYearStart = 2027;
        spring.Term = AcademicTerm.Spring;
        await db.SaveChangesAsync();

        var programs = await GetOpenProgramsAsync(db, new OpenProgramSearchQueryDto
        {
            AcademicYearStart = 2027,
            Term = AcademicTerm.Spring
        });

        var match = Assert.Single(programs);
        Assert.Equal(spring.ProgramOfferingId, match.ProgramOfferingId);
        Assert.DoesNotContain(programs, item => item.ProgramOfferingId == fall.ProgramOfferingId);
    }

    [Theory]
    [InlineData("search")]
    [InlineData("year")]
    [InlineData("term")]
    public async Task Invalid_open_program_search_is_rejected_before_querying(string invalidField)
    {
        await using var db = TestDb.Create();
        var request = invalidField switch
        {
            "search" => new OpenProgramSearchQueryDto
            {
                Search = new string('a', OpenProgramSearchQueryDto.MaximumSearchLength + 1)
            },
            "year" => new OpenProgramSearchQueryDto
            {
                AcademicYearStart = 1999
            },
            "term" => new OpenProgramSearchQueryDto
            {
                Term = AcademicTerm.LegacyUnspecified
            },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField))
        };
        var controller = new ProgramsController(db, new TestTimeProvider(Now));

        var actionResult = await controller.GetOpen(request, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Empty(db.ProgramOfferings);
    }

    private static async Task<IReadOnlyList<OpenProgramDto>> GetOpenProgramsAsync(
        GraduateAppDbContext db,
        OpenProgramSearchQueryDto? request = null)
    {
        var controller = new ProgramsController(db, new TestTimeProvider(Now));
        var actionResult = await controller.GetOpen(
            request ?? new OpenProgramSearchQueryDto(),
            CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        return Assert.IsAssignableFrom<IReadOnlyList<OpenProgramDto>>(ok.Value);
    }

    private static async Task<ProgramOffering> SeedFullOfferingAsync(
        GraduateAppDbContext db,
        bool usesEvaluationWorkflow,
        DocumentRequirementScenario documentRequirementScenario = DocumentRequirementScenario.ActiveRequired,
        bool includeSubmittedApplication = true)
    {
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

        if (documentRequirementScenario != DocumentRequirementScenario.None)
        {
            offering.DocumentRequirements.Add(DocumentRequirement(
                isRequired: documentRequirementScenario is DocumentRequirementScenario.InactiveRequired
                    or DocumentRequirementScenario.ActiveRequired,
                isActive: documentRequirementScenario is DocumentRequirementScenario.ActiveOptional
                    or DocumentRequirementScenario.ActiveRequired));
        }

        if (includeSubmittedApplication)
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
        }

        db.ProgramOfferings.Add(offering);
        await db.SaveChangesAsync();
        return offering;
    }

    private static ProgramOfferingDocumentRequirement DocumentRequirement(
        bool isRequired,
        bool isActive) => new()
        {
            PublicId = Guid.NewGuid(),
            DocumentCode = "DOC",
            NormalizedDocumentCode = "DOC",
            DisplayName = "Başvuru belgesi",
            IsRequired = isRequired,
            IsActive = isActive,
            AllowedContentCategory = DocumentContentCategory.PdfOnly,
            MaximumBytes = 1024,
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        };

    public enum DocumentRequirementScenario
    {
        None,
        ActiveOptional,
        InactiveRequired,
        ActiveRequired
    }
}
