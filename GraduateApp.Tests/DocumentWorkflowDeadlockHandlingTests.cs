using System.Data.Common;
using System.Linq.Expressions;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed class DocumentWorkflowDeadlockHandlingTests
{
    [LocalDbTheory]
    [InlineData(DeadlockStage.Query)]
    [InlineData(DeadlockStage.SaveChanges)]
    [InlineData(DeadlockStage.Commit)]
    public async Task Offering_update_maps_deadlock_from_the_entire_transaction_to_safe_conflict(
        DeadlockStage stage)
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppOfferingDeadlockBoundary_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var seeded = await SeedAsync(database.ConnectionString);
        var deadlock = await LocalDbTestSupport.CreateDeadlockExceptionAsync();
        var interceptor = CreateStageInterceptor(stage, deadlock);

        await using (var db = CreateContext(database.ConnectionString, interceptor))
        {
            var result = await CreateOfferingService(db).UpdateAsync(
                seeded.OfferingId,
                1,
                OpenRequest(seeded),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
            Assert.Equal(
                "İlan başka bir kullanıcı tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                result.Error);
            Assert.DoesNotContain("1205", result.Error, StringComparison.Ordinal);
            Assert.DoesNotContain("deadlock", result.Error, StringComparison.OrdinalIgnoreCase);
        }

        await AssertUnchangedWithoutAuditAsync(database.ConnectionString, seeded);
    }

    [LocalDbFact]
    public async Task Requirement_deactivation_maps_wrapped_query_deadlock_to_safe_conflict()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppRequirementDeadlockBoundary_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var seeded = await SeedAsync(database.ConnectionString);
        var deadlock = await LocalDbTestSupport.CreateDeadlockExceptionAsync();
        var interceptor = new ThrowingQueryExpressionInterceptor(
            () => new InvalidOperationException("Wrapped query failure.", deadlock));

        await using (var db = CreateContext(database.ConnectionString, interceptor))
        {
            var result = await CreateRequirementService(db).SetActiveAsync(
                seeded.OfferingId,
                seeded.RequirementPublicId,
                1,
                new DocumentRequirementActiveDto
                {
                    IsActive = false,
                    RowVersion = seeded.RequirementRowVersion
                },
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
            Assert.Equal(
                "Belge koşulu veya ilan başka bir yönetici tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                result.Error);
        }

        await AssertUnchangedWithoutAuditAsync(database.ConnectionString, seeded);
    }

    [Fact]
    public async Task Offering_update_deterministically_maps_sql_deadlock_to_safe_conflict()
    {
        var deadlock = TestSqlExceptionFactory.Create(1205);
        await using var db = TestDb.Create(new ThrowingQueryExpressionInterceptor(() => deadlock));

        var result = await CreateOfferingService(db).UpdateAsync(
            1,
            1,
            EmptyUpdateRequest(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.DoesNotContain("1205", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("deadlock", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Offering_update_does_not_convert_mixed_deadlock_and_unavailable_error_to_conflict()
    {
        var mixed = TestSqlExceptionFactory.Create(1205, 40197);
        await using var db = TestDb.Create(new ThrowingQueryExpressionInterceptor(() => mixed));

        var exception = await Assert.ThrowsAsync<SqlException>(() =>
            CreateOfferingService(db).UpdateAsync(
                1,
                1,
                EmptyUpdateRequest(),
                CancellationToken.None));

        Assert.Same(mixed, exception);
    }

    [Fact]
    public async Task Offering_update_does_not_convert_request_cancellation_to_conflict()
    {
        await using var db = TestDb.Create(new ThrowingQueryExpressionInterceptor(
            () => new OperationCanceledException("Request cancelled.")));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateOfferingService(db).UpdateAsync(
                1,
                1,
                EmptyUpdateRequest(),
                CancellationToken.None));
    }

    [Fact]
    public async Task Offering_update_does_not_hide_non_deadlock_query_exception()
    {
        await using var db = TestDb.Create(new ThrowingQueryExpressionInterceptor(
            () => new InvalidOperationException("Unexpected query failure.")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateOfferingService(db).UpdateAsync(
                1,
                1,
                EmptyUpdateRequest(),
                CancellationToken.None));

        Assert.Equal("Unexpected query failure.", exception.Message);
    }

    [LocalDbFact]
    public async Task Offering_update_does_not_convert_non_deadlock_sql_exception_to_conflict()
    {
        var sqlException = await LocalDbTestSupport.CreateUniqueViolationExceptionAsync(2601);
        await using var db = TestDb.Create(new ThrowingQueryExpressionInterceptor(() => sqlException));

        var exception = await Assert.ThrowsAsync<SqlException>(() =>
            CreateOfferingService(db).UpdateAsync(
                1,
                1,
                EmptyUpdateRequest(),
                CancellationToken.None));

        Assert.Equal(2601, exception.Number);
    }

    private static IInterceptor CreateStageInterceptor(DeadlockStage stage, SqlException deadlock) =>
        stage switch
        {
            DeadlockStage.Query => new ThrowingQueryExpressionInterceptor(
                () => new InvalidOperationException("Wrapped query failure.", deadlock)),
            DeadlockStage.SaveChanges => new ThrowingSaveChangesInterceptor(
                () => new DbUpdateException("Wrapped save failure.", deadlock)),
            DeadlockStage.Commit => new ThrowingCommitInterceptor(
                () => new InvalidOperationException("Wrapped commit failure.", deadlock)),
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };

    private static async Task<SeededAggregate> SeedAsync(string connectionString)
    {
        await using var db = CreateContext(connectionString);
        var now = new DateTime(2026, 7, 17, 9, 0, 0, DateTimeKind.Utc);
        var program = new GraduateApp.API.Models.Program
        {
            ProgramName = "Bilgisayar Mühendisliği",
            DegreeType = "Tezli Yüksek Lisans",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Institute = new Institute
            {
                InstituteName = "Fen Bilimleri",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
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
            IsOpen = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var requirement = new ProgramOfferingDocumentRequirement
        {
            PublicId = Guid.NewGuid(),
            DocumentCode = "TRANSCRIPT",
            NormalizedDocumentCode = "TRANSCRIPT",
            DisplayName = "Transkript",
            IsRequired = true,
            IsActive = true,
            AllowedContentCategory = DocumentContentCategory.PdfOnly,
            MaximumBytes = 1024,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        offering.DocumentRequirements.Add(requirement);
        db.ProgramOfferings.Add(offering);
        await db.SaveChangesAsync();
        return new SeededAggregate(
            offering.ProgramOfferingId,
            program.ProgramId,
            offering.AcademicYearStart,
            offering.Term,
            offering.ApplicationStartUtc.Value,
            offering.ApplicationDeadlineUtc.Value,
            offering.Quota,
            Convert.ToBase64String(offering.RowVersion),
            requirement.PublicId,
            Convert.ToBase64String(requirement.RowVersion));
    }

    private static async Task AssertUnchangedWithoutAuditAsync(
        string connectionString,
        SeededAggregate seeded)
    {
        await using var verification = CreateContext(connectionString);
        var offering = await verification.ProgramOfferings.AsNoTracking()
            .SingleAsync(item => item.ProgramOfferingId == seeded.OfferingId);
        var requirement = await verification.ProgramOfferingDocumentRequirements.AsNoTracking()
            .SingleAsync(item => item.PublicId == seeded.RequirementPublicId);
        Assert.False(offering.IsOpen);
        Assert.True(requirement.IsActive);
        Assert.True(requirement.IsRequired);
        Assert.Empty(await verification.SecurityAuditLogs.AsNoTracking().ToArrayAsync());
    }

    private static ProgramOfferingUpdateDto OpenRequest(SeededAggregate seeded) => new()
    {
        ProgramId = seeded.ProgramId,
        AcademicYearStart = seeded.AcademicYearStart,
        Term = seeded.Term,
        ApplicationStartUtc = seeded.ApplicationStartUtc,
        ApplicationDeadlineUtc = seeded.ApplicationDeadlineUtc,
        Quota = seeded.Quota,
        IsOpen = true,
        RowVersion = seeded.OfferingRowVersion
    };

    private static ProgramOfferingUpdateDto EmptyUpdateRequest() => new()
    {
        ProgramId = 1,
        AcademicYearStart = 2026,
        Term = AcademicTerm.Fall,
        ApplicationStartUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        ApplicationDeadlineUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        Quota = 1,
        RowVersion = "AQ=="
    };

    private static GraduateAppDbContext CreateContext(
        string connectionString,
        params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(connectionString);
        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }

        return new GraduateAppDbContext(options.Options);
    }

    private static ProgramOfferingService CreateOfferingService(GraduateAppDbContext db) => new(
        db,
        new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)));

    private static OfferingDocumentRequirementService CreateRequirementService(GraduateAppDbContext db) => new(
        db,
        new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)),
        Options.Create(new DocumentUploadOptions { MaximumBytes = 1024 * 1024 }));

    public enum DeadlockStage
    {
        Query,
        SaveChanges,
        Commit
    }

    private sealed record SeededAggregate(
        int OfferingId,
        int ProgramId,
        int AcademicYearStart,
        AcademicTerm Term,
        DateTime ApplicationStartUtc,
        DateTime ApplicationDeadlineUtc,
        int Quota,
        string OfferingRowVersion,
        Guid RequirementPublicId,
        string RequirementRowVersion);

    private sealed class ThrowingQueryExpressionInterceptor(Func<Exception> exceptionFactory)
        : IQueryExpressionInterceptor
    {
        public Expression QueryCompilationStarting(
            Expression queryExpression,
            QueryExpressionEventData eventData) =>
            throw exceptionFactory();
    }

    private sealed class ThrowingCommitInterceptor(Func<Exception> exceptionFactory)
        : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult>(exceptionFactory());
    }
}
