using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace GraduateApp.Tests;

public sealed class EvaluationOfferingConfigurationConcurrencyIntegrationTests(
    ITestOutputHelper output)
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
        var linearizedExamRequirementAttempts = await OpenThenCloseAndMakeExamOptionalAsync(
            database.ConnectionString,
            aggregates[2]);

        AssertExamRequirementReconfigurationAttempts(examRequirementAttempts);
        AssertPolicyDeleteAttempts(policyAttempts);
        AssertLinearizedExamRequirementReconfigurationAttempts(linearizedExamRequirementAttempts);

        var examRequirementState = await LoadFinalStateAsync(
            database.ConnectionString,
            aggregates[0]);
        var policyState = await LoadFinalStateAsync(
            database.ConnectionString,
            aggregates[1]);
        var linearizedExamRequirementState = await LoadFinalStateAsync(
            database.ConnectionString,
            aggregates[2]);
        output.WriteLine(
            "Exam requirement race: {0}. Final state: {1}.",
            DescribeAttempts(examRequirementAttempts),
            examRequirementState);
        output.WriteLine(
            "Policy delete race: {0}. Final state: {1}.",
            DescribeAttempts(policyAttempts),
            policyState);
        output.WriteLine(
            "Linearized exam requirement reconfiguration: {0}. Final state: {1}.",
            DescribeAttempts(linearizedExamRequirementAttempts),
            linearizedExamRequirementState);
        AssertExamRequirementFinalState(examRequirementAttempts, examRequirementState);
        AssertPolicyFinalState(policyAttempts, policyState);
        AssertLinearizedExamRequirementFinalState(
            linearizedExamRequirementAttempts,
            linearizedExamRequirementState);
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

    private static async Task<IReadOnlyList<Attempt>> OpenThenCloseAndMakeExamOptionalAsync(
        string connectionString,
        AggregateState aggregate)
    {
        var open = await OpenAsync(connectionString, aggregate)();
        var currentRowVersion = await LoadOfferingRowVersionAsync(
            connectionString,
            aggregate.OfferingId);
        var closeAndReconfigure = await MakeExamOptionalAsync(
            connectionString,
            aggregate with { OfferingRowVersion = currentRowVersion })();
        return [open, closeAndReconfigure];
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
            return new("Open", result.IsSuccess, result.StatusCode);
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
            return new("CloseAndMakeAlesOptional", result.IsSuccess, result.StatusCode);
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
            return new("DeleteCriterion", result.IsSuccess, result.StatusCode);
        };

    private static void AssertExamRequirementReconfigurationAttempts(IReadOnlyList<Attempt> attempts)
    {
        var diagnostics = DescribeAttempts(attempts);
        Assert.True(
            attempts.Any(item => item.Succeeded),
            $"Exam requirement yarışında en az bir işlem başarılı olmalıdır. {diagnostics}");
        AssertFailedAttemptsAreConflicts(attempts, diagnostics);
    }

    private static void AssertPolicyDeleteAttempts(IReadOnlyList<Attempt> attempts)
    {
        var diagnostics = DescribeAttempts(attempts);
        Assert.False(
            attempts.All(item => item.Succeeded),
            $"Açma ve kriter silme birlikte başarılı olamaz. {diagnostics}");
        AssertFailedAttemptsAreConflicts(attempts, diagnostics);
    }

    private static void AssertLinearizedExamRequirementReconfigurationAttempts(
        IReadOnlyList<Attempt> attempts)
    {
        var diagnostics = DescribeAttempts(attempts);
        Assert.True(
            attempts.All(item => item.Succeeded),
            $"Açma ve ardından kapatma+optional yapılandırma güncel RowVersion ile başarılı olmalıdır. {diagnostics}");
    }

    private static void AssertFailedAttemptsAreConflicts(
        IReadOnlyList<Attempt> attempts,
        string diagnostics)
    {
        foreach (var attempt in attempts.Where(item => !item.Succeeded))
        {
            Assert.True(
                attempt.StatusCode == StatusCodes.Status409Conflict,
                $"{attempt.Operation} güvenli 409 dönmelidir. {diagnostics}");
        }
    }

    private static void AssertExamRequirementFinalState(
        IReadOnlyList<Attempt> attempts,
        FinalConfigurationState state)
    {
        var diagnostics = $"{DescribeAttempts(attempts)} Final state: {state}.";
        Assert.Equal(1, state.ExamRequirementCount);
        Assert.NotNull(state.AlesIsRequired);
        Assert.Equal(EvaluationScoring.TotalWeightBasisPoints, state.TotalCriterionWeight);
        Assert.True(
            !state.IsOpen || state.AlesIsRequired == true,
            $"Açık ilan ALES koşulunu zorunlu tutmalıdır. {diagnostics}");
        Assert.True(
            !state.IsOpen || state.OrphanExamCriterionCount == 0,
            $"Açık ilan orphan ExamScore kriteri içeremez. {diagnostics}");
        Assert.Equal(
            attempts.Count(item => item.Succeeded),
            state.ProgramOfferingUpdatedAuditCount);
    }

    private static void AssertPolicyFinalState(
        IReadOnlyList<Attempt> attempts,
        FinalConfigurationState state)
    {
        var diagnostics = $"{DescribeAttempts(attempts)} Final state: {state}.";
        Assert.True(
            !state.IsOpen
                || (state.TotalCriterionWeight == EvaluationScoring.TotalWeightBasisPoints
                    && state.OrphanExamCriterionCount == 0),
            $"Policy delete yarışı açık ve geçersiz bir final state üretemez. {diagnostics}");
    }

    private static void AssertLinearizedExamRequirementFinalState(
        IReadOnlyList<Attempt> attempts,
        FinalConfigurationState state)
    {
        var diagnostics = $"{DescribeAttempts(attempts)} Final state: {state}.";
        Assert.False(state.IsOpen);
        Assert.Equal(1, state.ExamRequirementCount);
        Assert.False(state.AlesIsRequired);
        Assert.Equal(EvaluationScoring.TotalWeightBasisPoints, state.TotalCriterionWeight);
        Assert.Equal(1, state.OrphanExamCriterionCount);
        Assert.Equal(2, state.ProgramOfferingUpdatedAuditCount);
        Assert.True(
            !state.IsOpen || state.OrphanExamCriterionCount == 0,
            $"İki başarılı işlem kapalı ve güvenli bir final state bırakmalıdır. {diagnostics}");
    }

    private static async Task<FinalConfigurationState> LoadFinalStateAsync(
        string connectionString,
        AggregateState aggregate)
    {
        await using var db = CreateContext(connectionString);
        var offering = await db.ProgramOfferings.AsNoTracking()
            .SingleAsync(item => item.ProgramOfferingId == aggregate.OfferingId);
        var requirements = await db.ProgramOfferingExamRequirements.AsNoTracking()
            .Where(item => item.ProgramOfferingId == aggregate.OfferingId)
            .ToListAsync();
        var criteria = await db.ProgramOfferingEvaluationCriteria.AsNoTracking()
            .Where(item => item.ProgramOfferingId == aggregate.OfferingId)
            .ToListAsync();
        var requiredExamIds = requirements
            .Where(item => item.IsRequired)
            .Select(item => item.ExamId)
            .ToHashSet();
        var programOfferingUpdatedAuditCount = await db.SecurityAuditLogs.AsNoTracking()
            .CountAsync(item =>
                item.EventType == "ProgramOfferingUpdated"
                && item.TargetType == "ProgramOffering"
                && item.TargetId == aggregate.OfferingId.ToString());
        return new(
            offering.IsOpen,
            requirements.Count,
            requirements.SingleOrDefault(item => item.ExamId == aggregate.ExamId)?.IsRequired,
            criteria.Sum(item => item.WeightBasisPoints),
            criteria.Count(item =>
                item.SourceType == EvaluationCriterionSourceType.ExamScore
                && (!item.ExamId.HasValue || !requiredExamIds.Contains(item.ExamId.Value))),
            programOfferingUpdatedAuditCount);
    }

    private static string DescribeAttempts(IEnumerable<Attempt> attempts) =>
        string.Join(
            ", ",
            attempts.Select(item =>
                $"{item.Operation}: success={item.Succeeded}, status={item.StatusCode}"));

    private static async Task<string> LoadOfferingRowVersionAsync(
        string connectionString,
        int offeringId)
    {
        await using var db = CreateContext(connectionString);
        return Convert.ToBase64String(await db.ProgramOfferings.AsNoTracking()
            .Where(item => item.ProgramOfferingId == offeringId)
            .Select(item => item.RowVersion)
            .SingleAsync());
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
        for (var offset = 0; offset < 3; offset++)
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

    private sealed record FinalConfigurationState(
        bool IsOpen,
        int ExamRequirementCount,
        bool? AlesIsRequired,
        int TotalCriterionWeight,
        int OrphanExamCriterionCount,
        int ProgramOfferingUpdatedAuditCount);

    private sealed record Attempt(string Operation, bool Succeeded, int StatusCode);
}
