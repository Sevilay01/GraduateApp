using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.Tests;

public sealed class EvaluationOfferingConfigurationConcurrencyIntegrationTests
{
    [LocalDbFact]
    public async Task Opening_cannot_race_exam_requirement_or_policy_mutation_into_an_invalid_open_state()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppEvaluationInvariant_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var aggregates = await SeedAggregatesAsync(database.ConnectionString);

        var examRequirementAttempts = await RunConcurrentlyAsync(
            OpenAsync(database.ConnectionString, aggregates[0]),
            MakeExamOptionalAsync(database.ConnectionString, aggregates[0]));
        var policyAttempts = await RunConcurrentlyAsync(
            OpenAsync(database.ConnectionString, aggregates[1]),
            DeleteCriterionAsync(database.ConnectionString, aggregates[1]));

        AssertSafeConflict(examRequirementAttempts);
        AssertSafeConflict(policyAttempts);
        foreach (var aggregate in aggregates)
        {
            Assert.Equal(0, await CountInvalidOpenOfferingsAsync(database, aggregate.OfferingId));
        }
    }

    private static async Task<IReadOnlyList<Attempt>> RunConcurrentlyAsync(
        Func<Task<Attempt>> first,
        Func<Task<Attempt>> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = RunAfterSignalAsync(first, start.Task);
        var secondTask = RunAfterSignalAsync(second, start.Task);
        start.SetResult();
        return await Task.WhenAll(firstTask, secondTask);
    }

    private static async Task<Attempt> RunAfterSignalAsync(Func<Task<Attempt>> action, Task start)
    {
        await start;
        return await action();
    }

    private static Func<Task<Attempt>> OpenAsync(string connectionString, AggregateState aggregate) =>
        async () =>
        {
            await using var db = CreateContext(connectionString);
            var result = await CreateOfferingService(db).UpdateAsync(
                aggregate.OfferingId,
                1,
                OfferingRequest(aggregate, isOpen: true, examIsRequired: true),
                CancellationToken.None);
            return new(result.IsSuccess, result.StatusCode);
        };

    private static Func<Task<Attempt>> MakeExamOptionalAsync(string connectionString, AggregateState aggregate) =>
        async () =>
        {
            await using var db = CreateContext(connectionString);
            var result = await CreateOfferingService(db).UpdateAsync(
                aggregate.OfferingId,
                1,
                OfferingRequest(aggregate, isOpen: false, examIsRequired: false),
                CancellationToken.None);
            return new(result.IsSuccess, result.StatusCode);
        };

    private static Func<Task<Attempt>> DeleteCriterionAsync(string connectionString, AggregateState aggregate) =>
        async () =>
        {
            await using var db = CreateContext(connectionString);
            var result = await CreatePolicyService(db).DeleteAsync(
                aggregate.OfferingId,
                aggregate.CriterionPublicId,
                1,
                new EvaluationCriterionDeleteDto { RowVersion = aggregate.CriterionRowVersion },
                CancellationToken.None);
            return new(result.IsSuccess, result.StatusCode);
        };

    private static void AssertSafeConflict(IReadOnlyList<Attempt> attempts)
    {
        Assert.False(attempts.All(item => item.Succeeded));
        Assert.All(attempts, attempt =>
        {
            if (!attempt.Succeeded)
            {
                Assert.Equal(StatusCodes.Status409Conflict, attempt.StatusCode);
            }
        });
    }

    private static Task<int> CountInvalidOpenOfferingsAsync(LocalDbTestDatabase database, int offeringId) =>
        database.ScalarAsync<int>($"""
            SELECT COUNT(*)
            FROM [dbo].[ProgramOfferings] AS offering
            WHERE offering.[ProgramOfferingID] = {offeringId}
              AND offering.[IsOpen] = 1
              AND offering.[UsesEvaluationWorkflow] = 1
              AND
              (
                  COALESCE(
                      (
                          SELECT SUM(criterion.[WeightBasisPoints])
                          FROM [dbo].[ProgramOfferingEvaluationCriteria] AS criterion
                          WHERE criterion.[ProgramOfferingID] = offering.[ProgramOfferingID]
                      ),
                      0) <> 10000
                  OR EXISTS
                  (
                      SELECT 1
                      FROM [dbo].[ProgramOfferingEvaluationCriteria] AS criterion
                      WHERE criterion.[ProgramOfferingID] = offering.[ProgramOfferingID]
                        AND criterion.[SourceType] = 'ExamScore'
                        AND NOT EXISTS
                        (
                            SELECT 1
                            FROM [dbo].[ProgramOfferingExamRequirements] AS requirement
                            WHERE requirement.[ProgramOfferingID] = offering.[ProgramOfferingID]
                              AND requirement.[ExamID] = criterion.[ExamID]
                              AND requirement.[IsRequired] = 1
                        )
                  )
              );
            """);

    private static async Task<IReadOnlyList<AggregateState>> SeedAggregatesAsync(string connectionString)
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
        var exam = new Exam { ExamName = "ALES" };
        db.Programs.Add(program);
        for (var offset = 0; offset < 2; offset++)
        {
            var offering = new ProgramOffering
            {
                Program = program,
                AcademicYearStart = 2026 + offset,
                Term = AcademicTerm.Fall,
                ApplicationStartUtc = new DateTime(2026 + offset, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                ApplicationDeadlineUtc = new DateTime(2026 + offset, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                Quota = 10,
                IsOpen = false,
                UsesEvaluationWorkflow = true,
                EvaluationState = OfferingEvaluationState.Configuring,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            offering.DocumentRequirements.Add(new ProgramOfferingDocumentRequirement
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
            });
            offering.ExamRequirements.Add(new ProgramOfferingExamRequirement
            {
                Exam = exam,
                MinimumScore = 0m,
                IsRequired = true
            });
            offering.EvaluationCriteria.Add(new ProgramOfferingEvaluationCriterion
            {
                PublicId = Guid.NewGuid(),
                Code = "ALES",
                NormalizedCode = "ALES",
                DisplayName = "ALES",
                SourceType = EvaluationCriterionSourceType.ExamScore,
                Exam = exam,
                WeightBasisPoints = 10000,
                MaximumRawScore = 100m,
                TieBreakPriority = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            program.Offerings.Add(offering);
        }

        await db.SaveChangesAsync();
        return program.Offerings
            .OrderBy(item => item.AcademicYearStart)
            .Select(item =>
            {
                var criterion = item.EvaluationCriteria.Single();
                return new AggregateState(
                    item.ProgramOfferingId,
                    item.ProgramId,
                    item.AcademicYearStart,
                    item.Term,
                    item.ApplicationStartUtc!.Value,
                    item.ApplicationDeadlineUtc!.Value,
                    item.Quota,
                    item.ExamRequirements.Single().ExamId,
                    Convert.ToBase64String(item.RowVersion),
                    criterion.PublicId,
                    Convert.ToBase64String(criterion.RowVersion));
            })
            .ToArray();
    }

    private static ProgramOfferingUpdateDto OfferingRequest(
        AggregateState aggregate,
        bool isOpen,
        bool examIsRequired) => new()
        {
            ProgramId = aggregate.ProgramId,
            AcademicYearStart = aggregate.AcademicYearStart,
            Term = aggregate.Term,
            ApplicationStartUtc = aggregate.ApplicationStartUtc,
            ApplicationDeadlineUtc = aggregate.ApplicationDeadlineUtc,
            Quota = aggregate.Quota,
            IsOpen = isOpen,
            UsesEvaluationWorkflow = true,
            ExamRequirements =
            [
                new ProgramOfferingRequirementInputDto
                {
                    ExamId = aggregate.ExamId,
                    MinimumScore = 0m,
                    IsRequired = examIsRequired
                }
            ],
            RowVersion = aggregate.OfferingRowVersion
        };

    private static GraduateAppDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(connectionString)
            .Options);

    private static ProgramOfferingService CreateOfferingService(GraduateAppDbContext db) => new(
        db,
        new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)));

    private static EvaluationPolicyService CreatePolicyService(GraduateAppDbContext db) => new(
        db,
        new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)));

    private sealed record AggregateState(
        int OfferingId,
        int ProgramId,
        int AcademicYearStart,
        AcademicTerm Term,
        DateTime ApplicationStartUtc,
        DateTime ApplicationDeadlineUtc,
        int Quota,
        int ExamId,
        string OfferingRowVersion,
        Guid CriterionPublicId,
        string CriterionRowVersion);

    private sealed record Attempt(bool Succeeded, int StatusCode);
}
