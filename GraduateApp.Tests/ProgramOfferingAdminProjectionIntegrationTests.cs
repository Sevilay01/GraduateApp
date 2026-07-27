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
            academicYearStart: 2026,
            term: null,
            includeArchived: false,
            CancellationToken.None);

        var summaries = result.ToDictionary(item => item.ProgramName, StringComparer.Ordinal);
        Assert.Equal(5, summaries.Count);
        AssertSummary(summaries["Zero Requirements"], expectedCount: 0, expectedActiveRequired: false);
        AssertSummary(summaries["One Inactive Required"], expectedCount: 1, expectedActiveRequired: false);
        AssertSummary(summaries["One Active Optional"], expectedCount: 1, expectedActiveRequired: false);
        AssertSummary(summaries["One Active Required"], expectedCount: 1, expectedActiveRequired: true);
        AssertSummary(summaries["Many Requirements"], expectedCount: 3, expectedActiveRequired: true);

        var commandText = Assert.Single(commands.CommandTexts);
        Assert.Contains("COUNT(*)", commandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXISTS", commandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "[ProgramOfferingDocumentRequirements]",
            commandText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[DocumentCode]", commandText, StringComparison.Ordinal);
        Assert.DoesNotContain("[DisplayName]", commandText, StringComparison.Ordinal);
        Assert.DoesNotContain("[MaximumBytes]", commandText, StringComparison.Ordinal);
    }

    private static void AssertSummary(
        GraduateApp.API.DTOs.ProgramOfferingAdminDto summary,
        int expectedCount,
        bool expectedActiveRequired)
    {
        Assert.Equal(expectedCount, summary.DocumentRequirementCount);
        Assert.Equal(expectedActiveRequired, summary.HasActiveRequiredDocumentRequirement);
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
                (true, true)));
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
            IsOpen = false,
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
