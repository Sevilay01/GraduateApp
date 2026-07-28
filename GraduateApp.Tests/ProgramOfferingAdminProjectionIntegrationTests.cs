using System.Data.Common;
using System.Reflection;
using GraduateApp.API.Domain;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GraduateApp.Tests;

public sealed class ProgramOfferingAdminProjectionIntegrationTests
{
    [Fact]
    public void Admin_list_production_query_compiles_to_summary_subqueries_under_strict_ef_warnings()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppOfferingProjectionCompilation;Integrated Security=true;Encrypt=false")
            .ConfigureWarnings(warnings =>
                warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;
        using var db = new GraduateAppDbContext(options);
        var service = new ProgramOfferingService(db, TimeProvider.System);
        var method = typeof(ProgramOfferingService).GetMethod(
            "AdminListQuery",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var query = Assert.IsAssignableFrom<IQueryable>(
            method!.Invoke(service, [2026, null, false]));
        var commandText = query.ToQueryString();

        Assert.Contains("COUNT(*)", commandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXISTS", commandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "[ProgramOfferingDocumentRequirements]",
            commandText,
            StringComparison.Ordinal);
        Assert.Contains("[Applications]", commandText, StringComparison.Ordinal);
        Assert.Contains("[CurrentStatus]", commandText, StringComparison.Ordinal);
        Assert.DoesNotContain("[DocumentCode]", commandText, StringComparison.Ordinal);
        Assert.DoesNotContain("[DisplayName]", commandText, StringComparison.Ordinal);
        Assert.DoesNotContain("[MaximumBytes]", commandText, StringComparison.Ordinal);
    }

    [LocalDbFact]
    public async Task Admin_list_projects_document_summaries_in_one_sql_query_without_loading_full_requirements()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppOfferingProjection_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        await SeedAsync(database.ConnectionString);

        var commands = new ReaderCommandRecorder();
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .ConfigureWarnings(warnings =>
                warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .AddInterceptors(commands)
            .Options;
        await using var db = new GraduateAppDbContext(options);
        var service = new ProgramOfferingService(
            db,
            new TestTimeProvider(new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero)));

        var result = await service.GetForAdminAsync(
            academicYearStart: null,
            term: null,
            includeArchived: false,
            CancellationToken.None);

        var summaries = result.ToDictionary(item => item.ProgramName, StringComparer.Ordinal);
        Assert.Equal(8, summaries.Count);
        AssertSummary(summaries["Zero Requirements"], expectedCount: 0, expectedActiveRequiredCount: 0);
        AssertSummary(summaries["One Inactive Required"], expectedCount: 1, expectedActiveRequiredCount: 0);
        AssertSummary(summaries["One Active Optional"], expectedCount: 1, expectedActiveRequiredCount: 0);
        AssertSummary(summaries["One Active Required"], expectedCount: 1, expectedActiveRequiredCount: 1);
        AssertSummary(summaries["Many Requirements"], expectedCount: 4, expectedActiveRequiredCount: 2);
        Assert.Equal(
            GraduateApp.API.DTOs.OfferingDocumentConfigurationHealth.OpenInvalidNoApplications,
            summaries["Zero Requirements"].DocumentConfigurationHealth);
        Assert.Equal(
            GraduateApp.API.DTOs.OfferingDocumentConfigurationHealth.OpenInvalidWithDrafts,
            summaries["With Draft"].DocumentConfigurationHealth);
        Assert.Equal(1, summaries["With Draft"].DraftApplicationCount);
        Assert.Equal(
            GraduateApp.API.DTOs.OfferingDocumentConfigurationHealth.OpenInvalidWithSubmittedApplications,
            summaries["With Submitted"].DocumentConfigurationHealth);
        Assert.Equal(1, summaries["With Submitted"].SubmittedOrLaterApplicationCount);
        Assert.Equal(
            GraduateApp.API.DTOs.OfferingDocumentConfigurationHealth.LegacyOutsideDocumentWorkflow,
            summaries["Legacy Offering"].DocumentConfigurationHealth);

        var commandText = Assert.Single(commands.CommandTexts);
        Assert.Contains("COUNT(*)", commandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXISTS", commandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "[ProgramOfferingDocumentRequirements]",
            commandText,
            StringComparison.Ordinal);
        Assert.Contains("[Applications]", commandText, StringComparison.Ordinal);
        Assert.DoesNotContain("[DocumentCode]", commandText, StringComparison.Ordinal);
        Assert.DoesNotContain("[DisplayName]", commandText, StringComparison.Ordinal);
        Assert.DoesNotContain("[MaximumBytes]", commandText, StringComparison.Ordinal);
    }

    private static void AssertSummary(
        GraduateApp.API.DTOs.ProgramOfferingAdminDto summary,
        int expectedCount,
        int expectedActiveRequiredCount)
    {
        Assert.Equal(expectedCount, summary.DocumentRequirementCount);
        Assert.Equal(expectedActiveRequiredCount > 0, summary.HasActiveRequiredDocumentRequirement);
        Assert.Equal(expectedActiveRequiredCount, summary.ActiveRequiredDocumentRequirementCount);
    }

    private static async Task SeedAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var db = new GraduateAppDbContext(options);
        var now = new DateTime(2026, 7, 27, 9, 0, 0, DateTimeKind.Utc);
        var institute = new Institute
        {
            InstituteName = "Fen Bilimleri",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var student = CreateStudent("10000000146");
        var secondStudent = CreateStudent("10000000147");
        var withDraft = CreateProgram(institute, "With Draft", now);
        withDraft.Offerings.Single().Applications.Add(
            CreateApplication(student, ApplicationStatus.Draft, now));
        var withSubmitted = CreateProgram(institute, "With Submitted", now);
        withSubmitted.Offerings.Single().Applications.Add(
            CreateApplication(secondStudent, ApplicationStatus.Pending, now));
        var legacy = CreateProgram(institute, "Legacy Offering", now);
        legacy.Offerings.Single().AcademicYearStart = 0;
        legacy.Offerings.Single().Term = AcademicTerm.LegacyUnspecified;
        db.Students.AddRange(student, secondStudent);
        db.Programs.AddRange(
            CreateProgram(institute, "Zero Requirements", now),
            CreateProgram(institute, "One Inactive Required", now, (true, false)),
            CreateProgram(institute, "One Active Optional", now, (false, true)),
            CreateProgram(institute, "One Active Required", now, (true, true)),
            CreateProgram(
                institute,
                "Many Requirements",
                now,
                (true, false),
                (false, true),
                (true, true),
                (true, true)),
            withDraft,
            withSubmitted,
            legacy);
        await db.SaveChangesAsync();
    }

    private static GraduateApp.API.Models.Program CreateProgram(
        Institute institute,
        string programName,
        DateTime now,
        params (bool IsRequired, bool IsActive)[] requirements)
    {
        var program = new GraduateApp.API.Models.Program
        {
            ProgramName = programName,
            DegreeType = "Tezli Yüksek Lisans",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Institute = institute
        };
        var offering = new ProgramOffering
        {
            Program = program,
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall,
            ApplicationStartUtc = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc),
            ApplicationDeadlineUtc = new DateTime(2026, 8, 31, 17, 0, 0, DateTimeKind.Utc),
            Quota = 10,
            IsOpen = true,
            IsArchived = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        for (var index = 0; index < requirements.Length; index++)
        {
            var requirement = requirements[index];
            offering.DocumentRequirements.Add(new ProgramOfferingDocumentRequirement
            {
                PublicId = Guid.NewGuid(),
                DocumentCode = $"DOC-{index}",
                NormalizedDocumentCode = $"DOC-{index}",
                DisplayName = $"Belge {index}",
                IsRequired = requirement.IsRequired,
                IsActive = requirement.IsActive,
                AllowedContentCategory = DocumentContentCategory.PdfOnly,
                MaximumBytes = 1024,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        program.Offerings.Add(offering);
        return program;
    }

    private static Student CreateStudent(string tc) => new()
    {
        Tc = tc,
        PublicId = Guid.NewGuid(),
        StudentName = "Test",
        StudentSurname = "Öğrenci",
        Email = $"{tc}@example.test",
        NormalizedEmail = $"{tc}@EXAMPLE.TEST",
        PasswordHash = "hash",
        SecurityStamp = Guid.NewGuid().ToString("N"),
        IsActive = true
    };

    private static Application CreateApplication(
        Student student,
        ApplicationStatus status,
        DateTime now) => new()
        {
            PublicId = Guid.NewGuid(),
            Tc = student.Tc,
            TcNavigation = student,
            ApplicationDate = now,
            CurrentStatus = status.ToString(),
            UsesDocumentWorkflow = true
        };

    private sealed class ReaderCommandRecorder : DbCommandInterceptor
    {
        public List<string> CommandTexts { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandTexts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
