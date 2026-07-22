using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed class DocumentWorkflowConfigurationConcurrencyIntegrationTests
{
    [LocalDbFact]
    public async Task Opening_and_removing_the_last_required_requirement_are_safe_in_both_orders_and_concurrently()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppDocumentInvariant_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var aggregates = await SeedAggregatesAsync(database.ConnectionString);

        await RequirementFirstAsync(database.ConnectionString, aggregates[0]);
        await OpeningFirstAsync(database.ConnectionString, aggregates[1]);
        await ConcurrentlyAsync(database.ConnectionString, aggregates[2]);
        await StaleOfferingRowVersionAsync(database.ConnectionString, aggregates[3]);

        foreach (var aggregate in aggregates)
        {
            var invalidCount = await database.ScalarAsync<int>($"""
                SELECT COUNT(*)
                FROM [dbo].[ProgramOfferings] AS offering
                WHERE offering.[ProgramOfferingID] = {aggregate.OfferingId}
                  AND offering.[IsOpen] = 1
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM [dbo].[ProgramOfferingDocumentRequirements] AS requirement
                      WHERE requirement.[ProgramOfferingID] = offering.[ProgramOfferingID]
                        AND requirement.[IsActive] = 1
                        AND requirement.[IsRequired] = 1
                  );
                """);
            Assert.Equal(0, invalidCount);
        }
    }

    private static async Task RequirementFirstAsync(string connectionString, AggregateState aggregate)
    {
        await using (var db = CreateContext(connectionString))
        {
            var result = await CreateRequirementService(db).SetActiveAsync(
                aggregate.OfferingId,
                aggregate.RequirementPublicId,
                1,
                new DocumentRequirementActiveDto
                {
                    IsActive = false,
                    RowVersion = aggregate.RequirementRowVersion
                },
                CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using (var verification = CreateContext(connectionString))
        {
            var changedOfferingRowVersion = Convert.ToBase64String(
                (await verification.ProgramOfferings.AsNoTracking()
                    .SingleAsync(item => item.ProgramOfferingId == aggregate.OfferingId)).RowVersion);
            Assert.NotEqual(aggregate.OfferingRowVersion, changedOfferingRowVersion);
        }

        await using (var db = CreateContext(connectionString))
        {
            var result = await CreateOfferingService(db).UpdateAsync(
                aggregate.OfferingId,
                1,
                OpenRequest(aggregate),
                CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        }
    }

    private static async Task OpeningFirstAsync(string connectionString, AggregateState aggregate)
    {
        await using (var db = CreateContext(connectionString))
        {
            var result = await CreateOfferingService(db).UpdateAsync(
                aggregate.OfferingId,
                1,
                OpenRequest(aggregate),
                CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using (var db = CreateContext(connectionString))
        {
            var result = await CreateRequirementService(db).SetActiveAsync(
                aggregate.OfferingId,
                aggregate.RequirementPublicId,
                1,
                new DocumentRequirementActiveDto
                {
                    IsActive = false,
                    RowVersion = aggregate.RequirementRowVersion
                },
                CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
            Assert.Equal(
                "Açık bir ilanın son aktif zorunlu belge koşulu kaldırılamaz. Önce ilanı kapatın.",
                result.Error);
        }
    }

    private static async Task ConcurrentlyAsync(string connectionString, AggregateState aggregate)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opening = OpenAfterSignalAsync(connectionString, aggregate, start.Task);
        var deactivation = DeactivateAfterSignalAsync(connectionString, aggregate, start.Task);
        start.SetResult();

        var attempts = await Task.WhenAll(opening, deactivation);

        Assert.False(attempts.All(item => item.Succeeded));
        Assert.All(attempts, attempt =>
        {
            if (!attempt.Succeeded)
            {
                Assert.Equal(StatusCodes.Status409Conflict, attempt.StatusCode);
            }
        });
    }

    private static async Task StaleOfferingRowVersionAsync(string connectionString, AggregateState aggregate)
    {
        await using (var db = CreateContext(connectionString))
        {
            var result = await CreateRequirementService(db).SetActiveAsync(
                aggregate.OfferingId,
                aggregate.RequirementPublicId,
                1,
                new DocumentRequirementActiveDto
                {
                    IsActive = false,
                    RowVersion = aggregate.RequirementRowVersion
                },
                CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using (var db = CreateContext(connectionString))
        {
            var result = await CreateOfferingService(db).UpdateAsync(
                aggregate.OfferingId,
                1,
                OpenRequest(aggregate),
                CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
            Assert.Contains("başka bir kullanıcı", result.Error, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<Attempt> OpenAfterSignalAsync(
        string connectionString,
        AggregateState aggregate,
        Task start)
    {
        await start;
        await using var db = CreateContext(connectionString);
        var result = await CreateOfferingService(db).UpdateAsync(
            aggregate.OfferingId,
            1,
            OpenRequest(aggregate),
            CancellationToken.None);
        return new Attempt(result.IsSuccess, result.StatusCode);
    }

    private static async Task<Attempt> DeactivateAfterSignalAsync(
        string connectionString,
        AggregateState aggregate,
        Task start)
    {
        await start;
        await using var db = CreateContext(connectionString);
        var result = await CreateRequirementService(db).SetActiveAsync(
            aggregate.OfferingId,
            aggregate.RequirementPublicId,
            1,
            new DocumentRequirementActiveDto
            {
                IsActive = false,
                RowVersion = aggregate.RequirementRowVersion
            },
            CancellationToken.None);
        return new Attempt(result.IsSuccess, result.StatusCode);
    }

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
        db.Programs.Add(program);
        for (var offset = 0; offset < 4; offset++)
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
            if (offset == 3)
            {
                offering.DocumentRequirements.Add(new ProgramOfferingDocumentRequirement
                {
                    PublicId = Guid.NewGuid(),
                    DocumentCode = "DIPLOMA",
                    NormalizedDocumentCode = "DIPLOMA",
                    DisplayName = "Diploma",
                    IsRequired = true,
                    IsActive = true,
                    AllowedContentCategory = DocumentContentCategory.PdfOnly,
                    MaximumBytes = 1024,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }

            program.Offerings.Add(offering);
        }

        await db.SaveChangesAsync();
        return program.Offerings
            .OrderBy(item => item.AcademicYearStart)
            .Select(item => new AggregateState(
                item.ProgramOfferingId,
                item.ProgramId,
                item.AcademicYearStart,
                item.Term,
                item.ApplicationStartUtc!.Value,
                item.ApplicationDeadlineUtc!.Value,
                item.Quota,
                Convert.ToBase64String(item.RowVersion),
                item.DocumentRequirements.OrderBy(requirement => requirement.DocumentCode).Last().PublicId,
                Convert.ToBase64String(item.DocumentRequirements.OrderBy(requirement => requirement.DocumentCode).Last().RowVersion)))
            .ToArray();
    }

    private static ProgramOfferingUpdateDto OpenRequest(AggregateState aggregate) => new()
    {
        ProgramId = aggregate.ProgramId,
        AcademicYearStart = aggregate.AcademicYearStart,
        Term = aggregate.Term,
        ApplicationStartUtc = aggregate.ApplicationStartUtc,
        ApplicationDeadlineUtc = aggregate.ApplicationDeadlineUtc,
        Quota = aggregate.Quota,
        IsOpen = true,
        RowVersion = aggregate.OfferingRowVersion
    };

    private static GraduateAppDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(connectionString)
            .Options);

    private static ProgramOfferingService CreateOfferingService(GraduateAppDbContext db) => new(
        db,
        new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)));

    private static OfferingDocumentRequirementService CreateRequirementService(GraduateAppDbContext db) => new(
        db,
        new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)),
        Options.Create(new DocumentUploadOptions { MaximumBytes = 1024 * 1024 }));

    private sealed record AggregateState(
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

    private sealed record Attempt(bool Succeeded, int StatusCode);
}
