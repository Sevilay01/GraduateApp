using System.Data.Common;
using GraduateApp.API.Domain;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GraduateApp.Tests;

public sealed class StudentApplicationDetailQuerySplittingIntegrationTests
{
    [LocalDbFact]
    public async Task Foreign_application_stops_after_the_root_query_under_strict_collection_warnings()
    {
        await using var database = await CreateDatabaseAsync();
        var applicationPublicId = await SeedDocumentGraphAsync(database.ConnectionString);
        var commandCounter = new ReaderCommandCounter();

        await using var db = CreateContext(database.ConnectionString, commandCounter);
        var detail = await CreateService(db).GetDetailForStudentAsync(
            "10000000154",
            applicationPublicId,
            CancellationToken.None);

        Assert.Null(detail);
        Assert.Equal(1, commandCounter.Count);
    }

    [LocalDbFact]
    public async Task Owned_application_uses_three_split_queries_and_maps_unique_complete_documents()
    {
        await using var database = await CreateDatabaseAsync();
        var applicationPublicId = await SeedDocumentGraphAsync(database.ConnectionString);
        var commandCounter = new ReaderCommandCounter();

        await using var db = CreateContext(database.ConnectionString, commandCounter);
        var detail = await CreateService(db).GetDetailForStudentAsync(
            "10000000146",
            applicationPublicId,
            CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(3, commandCounter.Count);
        Assert.Equal(2, detail.DocumentRequirements.Count);
        Assert.Equal(
            detail.DocumentRequirements.Count,
            detail.DocumentRequirements.Select(item => item.PublicId).Distinct().Count());
        var currentDocuments = detail.DocumentRequirements
            .Select(item => item.CurrentDocument)
            .Where(item => item is not null)
            .ToArray();
        Assert.Equal(2, currentDocuments.Length);
        Assert.Equal(
            currentDocuments.Length,
            currentDocuments.Select(item => item!.PublicId).Distinct().Count());
        Assert.Contains(
            currentDocuments,
            item => item!.OriginalFileName == "transkript.pdf");
        Assert.Contains(
            currentDocuments,
            item => item!.OriginalFileName == "diploma.pdf");
    }

    [LocalDbFact]
    public async Task Admin_application_detail_uses_five_split_queries_and_maps_complete_collections()
    {
        await using var database = await CreateDatabaseAsync();
        var applicationPublicId = await SeedDocumentGraphAsync(
            database.ConnectionString,
            ApplicationStatus.Pending);
        var commandCounter = new ReaderCommandCounter();

        await using var db = CreateContext(database.ConnectionString, commandCounter);
        var detail = await CreateService(db).GetDetailForAdminAsync(
            applicationPublicId,
            CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(5, commandCounter.Count);
        Assert.Single(detail.History);
        Assert.Single(detail.ScoreSnapshots);
        Assert.Equal(2, detail.DocumentRequirements.Count);
        Assert.Equal(
            detail.DocumentRequirements.Count,
            detail.DocumentRequirements.Select(item => item.PublicId).Distinct().Count());
        Assert.Equal(
            2,
            detail.DocumentRequirements.Count(item => item.CurrentDocument is not null));
    }

    private static async Task<LocalDbTestDatabase> CreateDatabaseAsync()
    {
        var database = new LocalDbTestDatabase(
            $"GraduateAppStudentDetail_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        try
        {
            await database.CreateCurrentModelSchemaAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static GraduateAppDbContext CreateContext(
        string connectionString,
        ReaderCommandCounter commandCounter) =>
        new(new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(connectionString)
            .ConfigureWarnings(warnings =>
                warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .AddInterceptors(commandCounter)
            .Options);

    private static ApplicationService CreateService(GraduateAppDbContext db) =>
        new(db, new TestTimeProvider(new DateTimeOffset(2026, 7, 25, 9, 0, 0, TimeSpan.Zero)));

    private static async Task<Guid> SeedDocumentGraphAsync(
        string connectionString,
        ApplicationStatus status = ApplicationStatus.Draft)
    {
        await using var db = new GraduateAppDbContext(
            new DbContextOptionsBuilder<GraduateAppDbContext>()
                .UseSqlServer(connectionString)
                .Options);
        var now = new DateTime(2026, 7, 25, 9, 0, 0, DateTimeKind.Utc);
        var owner = CreateStudent("10000000146", "owner@example.test", now);
        var other = CreateStudent("10000000154", "other@example.test", now);
        var exam = new Exam { ExamName = "ALES" };
        var application = new Application
        {
            PublicId = Guid.NewGuid(),
            TcNavigation = owner,
            ProgramOffering = new ProgramOffering
            {
                Program = new GraduateApp.API.Models.Program
                {
                    ProgramName = "Bilgisayar Mühendisliği",
                    DegreeType = "Tezli Yüksek Lisans",
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    Institute = new Institute
                    {
                        InstituteName = "Fen Bilimleri Enstitüsü",
                        IsActive = true,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    }
                },
                AcademicYearStart = 2026,
                Term = AcademicTerm.Fall,
                ApplicationStartUtc = now.AddDays(-10),
                ApplicationDeadlineUtc = now.AddDays(10),
                Quota = 10,
                IsOpen = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            ApplicationDate = now,
            CurrentStatus = status.ToString(),
            UsesDocumentWorkflow = true,
            ApplicationStatusHistories =
            [
                new ApplicationStatusHistory
                {
                    PreviousStatus = ApplicationStatus.Draft.ToString(),
                    StatusName = status.ToString(),
                    ChangeDate = now,
                    Notes = "Test durum geçmişi."
                }
            ],
            ScoreSnapshots =
            [
                new ApplicationScoreSnapshot
                {
                    Exam = exam,
                    ExamNameSnapshot = exam.ExamName,
                    ScoreSnapshot = 82.5m,
                    ExamDateSnapshot = new DateOnly(2026, 6, 1),
                    CapturedAtUtc = now
                }
            ]
        };
        AddRequirement(application, "TRANSCRIPT", "Transkript", "transkript.pdf", now);
        AddRequirement(application, "DIPLOMA", "Diploma", "diploma.pdf", now);
        db.Students.Add(other);
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        return application.PublicId;
    }

    private static Student CreateStudent(string tc, string email, DateTime now) => new()
    {
        Tc = tc,
        PublicId = Guid.NewGuid(),
        StudentName = "Test",
        StudentSurname = "Öğrenci",
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        PasswordHash = "hash",
        SecurityStamp = Guid.NewGuid().ToString("N"),
        IsActive = true,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    private static void AddRequirement(
        Application application,
        string code,
        string displayName,
        string currentFileName,
        DateTime now)
    {
        var requirement = new ApplicationDocumentRequirementSnapshot
        {
            PublicId = Guid.NewGuid(),
            Application = application,
            DocumentCode = code,
            DisplayName = displayName,
            IsRequired = true,
            AllowedContentCategory = DocumentContentCategory.PdfOnly,
            MaximumBytes = 1024 * 1024
        };
        AddDocument(application, requirement, 1, false, $"old-{currentFileName}", now.AddDays(-1));
        AddDocument(application, requirement, 2, true, currentFileName, now);
        application.DocumentRequirementSnapshots.Add(requirement);
    }

    private static void AddDocument(
        Application application,
        ApplicationDocumentRequirementSnapshot requirement,
        int version,
        bool isCurrent,
        string fileName,
        DateTime uploadedAtUtc)
    {
        var document = new ApplicationDocument
        {
            PublicId = Guid.NewGuid(),
            Application = application,
            RequirementSnapshot = requirement,
            VersionNumber = version,
            IsCurrent = isCurrent,
            OriginalFileName = fileName,
            ObjectKey = Guid.NewGuid().ToString("N"),
            VerifiedContentType = "application/pdf",
            FileSize = 128,
            Sha256 = new string('a', 64),
            ReviewStatus = DocumentReviewStatus.Pending,
            UploadedAtUtc = uploadedAtUtc
        };
        requirement.Documents.Add(document);
        application.Documents.Add(document);
    }

    private sealed class ReaderCommandCounter : DbCommandInterceptor
    {
        private int count;

        public int Count => count;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref count);
            return ValueTask.FromResult(result);
        }
    }
}
