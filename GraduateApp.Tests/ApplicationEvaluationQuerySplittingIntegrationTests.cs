using System.Data.Common;
using System.Reflection;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GraduateApp.Tests;

public sealed class ApplicationEvaluationQuerySplittingIntegrationTests
{
    [Fact]
    public void SqlServer_query_compilation_identifies_unsplit_policy_shape_and_accepts_production_queries()
    {
        using var db = Context(
            "Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppQueryCompilation;Integrated Security=true;Encrypt=false",
            new ReaderCommandCounter());
        var unsplitPolicyQuery = db.ProgramOfferings
            .Include(item => item.Applications)
            .Include(item => item.ExamRequirements)
            .Include(item => item.EvaluationCriteria).ThenInclude(item => item.Exam)
            .Where(item => item.ProgramOfferingId == 42);

        var exception = Assert.Throws<InvalidOperationException>(
            () => unsplitPolicyQuery.ToQueryString());
        Assert.Contains(
            nameof(RelationalEventId.MultipleCollectionIncludeWarning),
            exception.Message,
            StringComparison.Ordinal);

        var evaluationService = new ApplicationEvaluationService(
            db,
            new TestTimeProvider(DateTimeOffset.UtcNow));
        var evaluationReadQuery = InvokeQuery(
                evaluationService,
                "AdminPageQuery",
                false)
            .Include(item => item.ExamRequirements).ThenInclude(item => item.Exam)
            .Where(item => item.ProgramOfferingId == 42);
        var policyService = new EvaluationPolicyService(
            db,
            new TestTimeProvider(DateTimeOffset.UtcNow));
        var policyQuery = InvokeQuery(policyService, "PolicyAggregateQuery")
            .Where(item => item.ProgramOfferingId == 42);

        Assert.Contains("SELECT", evaluationReadQuery.ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("SELECT", policyQuery.ToQueryString(), StringComparison.Ordinal);
    }

    [LocalDbFact]
    public async Task Evaluation_read_uses_fixed_split_queries_without_warning_or_duplicate_dtos()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppEvaluationRead_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var offeringId = await SeedAsync(
            database.ConnectionString,
            includeEvaluationComponents: true);
        var commandCounter = new ReaderCommandCounter();

        await using var db = Context(database.ConnectionString, commandCounter);
        var page = await new ApplicationEvaluationService(
            db,
            new TestTimeProvider(new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)))
            .GetAdminPageAsync(offeringId, CancellationToken.None);

        var result = Assert.IsType<AdminEvaluationPageDto>(page);
        AssertEvaluationReadDto(result, expectedApplicationCount: 2);
        AssertAdminReadCommands(commandCounter);

        await AddSubmittedApplicationAsync(database.ConnectionString, offeringId);
        var expandedCommandCounter = new ReaderCommandCounter();
        await using var expandedDb = Context(database.ConnectionString, expandedCommandCounter);
        var expandedPage = await new ApplicationEvaluationService(
            expandedDb,
            new TestTimeProvider(new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)))
            .GetAdminPageAsync(offeringId, CancellationToken.None);

        var expandedResult = Assert.IsType<AdminEvaluationPageDto>(expandedPage);
        AssertEvaluationReadDto(expandedResult, expectedApplicationCount: 3);
        AssertAdminReadCommands(expandedCommandCounter);
    }

    [LocalDbFact]
    public async Task Evaluation_read_loads_populated_nested_collections_without_duplicate_dtos()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppEvaluationNestedRead_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var offeringId = await SeedAsync(
            database.ConnectionString,
            includeEvaluationComponents: true,
            includeDocuments: true);
        var commandCounter = new ReaderCommandCounter();

        await using var db = Context(database.ConnectionString, commandCounter);
        var page = await new ApplicationEvaluationService(
            db,
            new TestTimeProvider(new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)))
            .GetAdminPageAsync(offeringId, CancellationToken.None);

        var result = Assert.IsType<AdminEvaluationPageDto>(page);
        AssertEvaluationReadDto(result, expectedApplicationCount: 2, expectDocuments: true);
        AssertNestedAdminReadCommands(commandCounter);
    }

    private static void AssertEvaluationReadDto(
        AdminEvaluationPageDto result,
        int expectedApplicationCount,
        bool expectDocuments = false)
    {
        Assert.Equal(3, result.Criteria.Count);
        Assert.Equal(3, result.Criteria.Select(item => item.PublicId).Distinct().Count());
        Assert.Equal(expectedApplicationCount, result.Applications.Count);
        Assert.Equal(
            expectedApplicationCount,
            result.Applications.Select(item => item.ApplicationPublicId).Distinct().Count());
        Assert.Collection(
            result.EligibleExamRequirements,
            item =>
            {
                Assert.Equal("ALES", item.ExamName);
                Assert.Equal(55.5m, item.MinimumScore);
                Assert.True(item.IsRequired);
            },
            item =>
            {
                Assert.Equal("YDS", item.ExamName);
                Assert.Equal(70m, item.MinimumScore);
                Assert.True(item.IsRequired);
            });
        Assert.DoesNotContain(
            result.EligibleExamRequirements,
            item => item.ExamName == "TOEFL");

        var examCriterion = Assert.Single(
            result.Criteria,
            item => item.SourceType == EvaluationCriterionSourceType.ExamScore);
        Assert.Equal(
            result.EligibleExamRequirements.Single(item => item.ExamName == "ALES").ExamId,
            examCriterion.ExamId);
        Assert.Null(Assert.Single(
            result.Criteria,
            item => item.SourceType == EvaluationCriterionSourceType.UndergraduateGpa).ExamId);
        Assert.Null(Assert.Single(
            result.Criteria,
            item => item.SourceType == EvaluationCriterionSourceType.ManualScore).ExamId);
        Assert.Equal(2, result.Applications.Count(item => item.Components.Count == 1));
        Assert.All(
            result.Applications.Where(item => item.Components.Count != 0),
            item => Assert.Equal(
                EvaluationCriterionSourceType.UndergraduateGpa,
                Assert.Single(item.Components).SourceType));

        if (expectDocuments)
        {
            Assert.Contains(
                result.Applications,
                item => item.DocumentReviewSummary == "1/1 zorunlu belge onaylı");
            Assert.Contains(
                result.Applications,
                item => item.DocumentReviewSummary == "0/1 zorunlu belge onaylı");
        }
        else
        {
            Assert.All(
                result.Applications,
                item => Assert.Equal("Legacy belge akışı", item.DocumentReviewSummary));
        }
    }

    private static void AssertAdminReadCommands(ReaderCommandCounter commandCounter)
    {
        string[] expectedCategories =
        [
            "offering/reference",
            "criteria/exam reference",
            "offering exam requirements",
            "applications/references",
            "evaluation components",
            "document requirement snapshots"
        ];
        AssertReaderCommands(commandCounter, expectedCategories);
    }

    private static void AssertNestedAdminReadCommands(ReaderCommandCounter commandCounter)
    {
        string[] expectedCategories =
        [
            "offering/reference",
            "criteria/exam reference",
            "offering exam requirements",
            "applications/references",
            "evaluation components",
            "document requirement snapshots",
            "documents"
        ];
        AssertReaderCommands(commandCounter, expectedCategories);
    }

    private static void AssertReaderCommands(
        ReaderCommandCounter commandCounter,
        string[] expectedCategories)
    {
        var actualCategories = commandCounter.CommandCategories
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            commandCounter.Count == expectedCategories.Length,
            $"Expected {expectedCategories.Length} reader commands but observed {commandCounter.Count}.{Environment.NewLine}{commandCounter.Diagnostic}");
        Assert.True(
            expectedCategories.OrderBy(item => item, StringComparer.Ordinal).SequenceEqual(actualCategories),
            $"Reader command categories differed.{Environment.NewLine}{commandCounter.Diagnostic}");
    }

    [LocalDbFact]
    public async Task Policy_aggregate_uses_four_split_queries_inside_existing_serializable_boundary()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppEvaluationPolicy_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var offeringId = await SeedAsync(database.ConnectionString);
        var commandCounter = new ReaderCommandCounter();

        await using var db = Context(database.ConnectionString, commandCounter);
        var result = await new EvaluationPolicyService(
            db,
            new TestTimeProvider(new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)))
            .CreateAsync(
                offeringId,
                7,
                new EvaluationCriterionCreateDto
                {
                    Code = "EXTRA",
                    DisplayName = "Ek kriter",
                    SourceType = EvaluationCriterionSourceType.ExamScore,
                    ExamId = int.MaxValue,
                    WeightBasisPoints = 1,
                    MaximumRawScore = 100m,
                    TieBreakPriority = 4
                },
                CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(
            commandCounter.Count == 4,
            $"Expected 4 reader commands but observed {commandCounter.Count}.{Environment.NewLine}{commandCounter.Diagnostic}");
    }

    private static GraduateAppDbContext Context(
        string connectionString,
        ReaderCommandCounter commandCounter) =>
        new(new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(connectionString)
            .ConfigureWarnings(warnings =>
                warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .AddInterceptors(commandCounter)
            .Options);

    private static IQueryable<ProgramOffering> InvokeQuery(
        object service,
        string methodName,
        params object[] arguments)
    {
        var method = service.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IQueryable<ProgramOffering>>(
            method.Invoke(service, arguments));
    }

    private static async Task<int> SeedAsync(
        string connectionString,
        bool includeEvaluationComponents = false,
        bool includeDocuments = false)
    {
        await using var db = new GraduateAppDbContext(
            new DbContextOptionsBuilder<GraduateAppDbContext>()
                .UseSqlServer(connectionString)
                .Options);
        var now = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        var ales = new Exam { ExamName = "ALES" };
        var yds = new Exam { ExamName = "YDS" };
        var toefl = new Exam { ExamName = "TOEFL" };
        var offering = new ProgramOffering
        {
            Program = new GraduateApp.API.Models.Program
            {
                ProgramName = "Test Programı",
                DegreeType = "Tezli Yüksek Lisans",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Institute = new Institute
                {
                    InstituteName = "Test Enstitüsü",
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                }
            },
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall,
            ApplicationStartUtc = now.AddMonths(-1),
            ApplicationDeadlineUtc = now.AddDays(1),
            Quota = 2,
            IsOpen = true,
            UsesEvaluationWorkflow = true,
            EvaluationState = OfferingEvaluationState.Configuring,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ExamRequirements =
            [
                new ProgramOfferingExamRequirement
                {
                    Exam = ales,
                    MinimumScore = 55.5m,
                    IsRequired = true
                },
                new ProgramOfferingExamRequirement
                {
                    Exam = yds,
                    MinimumScore = 70m,
                    IsRequired = true
                },
                new ProgramOfferingExamRequirement
                {
                    Exam = toefl,
                    MinimumScore = 80m,
                    IsRequired = false
                }
            ],
            EvaluationCriteria =
            [
                Criterion(
                    "GPA",
                    "Lisans GNO",
                    EvaluationCriterionSourceType.UndergraduateGpa,
                    3000,
                    4m,
                    1,
                    now),
                Criterion(
                    "ALES",
                    "ALES",
                    EvaluationCriterionSourceType.ExamScore,
                    4000,
                    100m,
                    2,
                    now,
                    ales),
                Criterion(
                    "INTERVIEW",
                    "Mülakat",
                    EvaluationCriterionSourceType.ManualScore,
                    3000,
                    100m,
                    3,
                    now)
            ]
        };
        var firstApplication = Application(
            offering,
            "10000000146",
            "Bir",
            new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc));
        var secondApplication = Application(
            offering,
            "10000000154",
            "İki",
            new DateTime(2026, 8, 1, 11, 0, 0, DateTimeKind.Utc));
        offering.Applications.Add(firstApplication);
        offering.Applications.Add(secondApplication);
        if (includeEvaluationComponents)
        {
            var gpaCriterion = offering.EvaluationCriteria.Single(
                item => item.SourceType == EvaluationCriterionSourceType.UndergraduateGpa);
            AddEvaluation(firstApplication, offering, gpaCriterion);
            AddEvaluation(secondApplication, offering, gpaCriterion);
        }

        if (includeDocuments)
        {
            AddDocument(firstApplication, now, DocumentReviewStatus.Approved);
            AddDocument(secondApplication, now, DocumentReviewStatus.Pending);
        }

        db.ProgramOfferings.Add(offering);
        await db.SaveChangesAsync();
        return offering.ProgramOfferingId;
    }

    private static async Task AddSubmittedApplicationAsync(
        string connectionString,
        int offeringId)
    {
        await using var db = new GraduateAppDbContext(
            new DbContextOptionsBuilder<GraduateAppDbContext>()
                .UseSqlServer(connectionString)
                .Options);
        var offering = await db.ProgramOfferings.SingleAsync(
            item => item.ProgramOfferingId == offeringId);
        offering.Applications.Add(Application(
            offering,
            "10000000162",
            "Üç",
            new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
    }

    private static ProgramOfferingEvaluationCriterion Criterion(
        string code,
        string displayName,
        EvaluationCriterionSourceType sourceType,
        int weight,
        decimal maximumRawScore,
        int priority,
        DateTime now,
        Exam? exam = null) =>
        new()
        {
            PublicId = Guid.NewGuid(),
            Code = code,
            NormalizedCode = code,
            DisplayName = displayName,
            SourceType = sourceType,
            Exam = exam,
            WeightBasisPoints = weight,
            MaximumRawScore = maximumRawScore,
            TieBreakPriority = priority,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

    private static Application Application(
        ProgramOffering offering,
        string tc,
        string surname,
        DateTime applicationDate) =>
        new()
        {
            PublicId = Guid.NewGuid(),
            TcNavigation = new Student
            {
                Tc = tc,
                PublicId = Guid.NewGuid(),
                StudentName = "Test",
                StudentSurname = surname,
                Email = $"{tc}@example.test",
                NormalizedEmail = $"{tc}@EXAMPLE.TEST",
                PasswordHash = "hash",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                IsActive = true,
                CreatedAtUtc = applicationDate,
                UpdatedAtUtc = applicationDate
            },
            ProgramOffering = offering,
            ApplicationDate = applicationDate,
            CurrentStatus = ApplicationStatus.UnderReview.ToString(),
            UsesDocumentWorkflow = false,
            UsesEvaluationWorkflow = true
        };

    private static void AddEvaluation(
        Application application,
        ProgramOffering offering,
        ProgramOfferingEvaluationCriterion criterion)
    {
        application.Evaluation = new ApplicationEvaluation
        {
            Application = application,
            ProgramOffering = offering,
            EligibilityStatus = EvaluationEligibilityStatus.Pending,
            Components =
            [
                new ApplicationEvaluationComponent
                {
                    SourceCriterion = criterion,
                    CriterionPublicIdSnapshot = criterion.PublicId,
                    CodeSnapshot = criterion.Code,
                    DisplayNameSnapshot = criterion.DisplayName,
                    SourceTypeSnapshot = criterion.SourceType,
                    RawScore = 3.5m,
                    MaximumRawScoreSnapshot = criterion.MaximumRawScore,
                    NormalizedScore = 87.5m,
                    WeightBasisPointsSnapshot = criterion.WeightBasisPoints,
                    WeightedScore = 26.25m,
                    TieBreakPrioritySnapshot = criterion.TieBreakPriority
                }
            ]
        };
    }

    private static void AddDocument(
        Application application,
        DateTime now,
        DocumentReviewStatus documentStatus)
    {
        application.UsesDocumentWorkflow = true;
        var snapshot = new ApplicationDocumentRequirementSnapshot
        {
            Application = application,
            DocumentCode = "TRANSCRIPT",
            DisplayName = "Transkript",
            IsRequired = true,
            AllowedContentCategory = DocumentContentCategory.PdfOnly,
            MaximumBytes = 1024
        };
        var document = new ApplicationDocument
        {
            Application = application,
            RequirementSnapshot = snapshot,
            VersionNumber = 1,
            IsCurrent = true,
            OriginalFileName = "transkript.pdf",
            ObjectKey = Guid.NewGuid().ToString("N"),
            VerifiedContentType = "application/pdf",
            FileSize = 100,
            Sha256 = new string('0', 64),
            ReviewStatus = documentStatus,
            UploadedAtUtc = now
        };
        snapshot.Documents.Add(document);
        application.DocumentRequirementSnapshots.Add(snapshot);
        application.Documents.Add(document);
    }

    private sealed class ReaderCommandCounter : DbCommandInterceptor
    {
        private readonly List<string> commandTexts = [];

        public int Count => commandTexts.Count;
        public IReadOnlyList<string> CommandTexts => commandTexts;
        public IReadOnlyList<string> CommandCategories =>
            commandTexts.Select(Classify).ToArray();
        public string Diagnostic => string.Join(
            Environment.NewLine,
            commandTexts.Select(
                (commandText, index) => $"{index + 1}. {Classify(commandText)}: {TableSummary(commandText)}"));

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            commandTexts.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            commandTexts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        private static string Classify(string commandText)
        {
            if (commandText.Contains("[ApplicationDocuments]", StringComparison.Ordinal))
            {
                return "documents";
            }

            if (commandText.Contains("[ApplicationEvaluationComponents]", StringComparison.Ordinal))
            {
                return "evaluation components";
            }

            if (commandText.Contains("[ApplicationDocumentRequirementSnapshots]", StringComparison.Ordinal))
            {
                return "document requirement snapshots";
            }

            if (commandText.Contains("[ProgramOfferingExamRequirements]", StringComparison.Ordinal))
            {
                return "offering exam requirements";
            }

            if (commandText.Contains("[ProgramOfferingEvaluationCriteria]", StringComparison.Ordinal))
            {
                return "criteria/exam reference";
            }

            if (commandText.Contains("[Applications]", StringComparison.Ordinal))
            {
                return "applications/references";
            }

            return commandText.Contains("[ProgramOfferings]", StringComparison.Ordinal)
                ? "offering/reference"
                : "unclassified reader";
        }

        private static string TableSummary(string commandText)
        {
            string[] knownTables =
            [
                "ProgramOfferings",
                "Programs",
                "ProgramOfferingEvaluationCriteria",
                "ProgramOfferingExamRequirements",
                "Exams",
                "Applications",
                "Students",
                "ApplicationEvaluations",
                "ApplicationEvaluationComponents",
                "ApplicationDocumentRequirementSnapshots",
                "ApplicationDocuments"
            ];
            var tables = knownTables
                .Where(table => commandText.Contains($"[{table}]", StringComparison.Ordinal))
                .ToArray();
            return tables.Length == 0 ? "no recognized tables" : string.Join(", ", tables);
        }
    }
}
