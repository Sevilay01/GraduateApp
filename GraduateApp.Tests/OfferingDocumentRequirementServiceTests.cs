using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed class OfferingDocumentRequirementServiceTests
{
    [Fact]
    public async Task Document_code_is_normalized_and_case_insensitive_duplicate_is_rejected()
    {
        await using var db = TestDb.Create();
        var offering = await SeedOfferingAsync(db);
        var service = CreateService(db);

        var first = await service.CreateAsync(offering.ProgramOfferingId, 1, Request(" transcript "), CancellationToken.None);
        var duplicate = await service.CreateAsync(offering.ProgramOfferingId, 1, Request("TRANSCRIPT"), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, duplicate.StatusCode);
        Assert.Equal("TRANSCRIPT", db.ProgramOfferingDocumentRequirements.Single().NormalizedDocumentCode);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "OfferingDocumentRequirementCreated");
    }

    [Fact]
    public async Task Requirement_limit_cannot_exceed_global_limit()
    {
        await using var db = TestDb.Create();
        var offering = await SeedOfferingAsync(db);
        var request = Request("TRANSCRIPT");
        var oversized = new OfferingDocumentRequirementCreateDto
        {
            DocumentCode = request.DocumentCode,
            DisplayName = request.DisplayName,
            IsRequired = request.IsRequired,
            AllowedContentCategory = request.AllowedContentCategory,
            MaximumBytes = 2049
        };

        var result = await CreateService(db).CreateAsync(offering.ProgramOfferingId, 1, oversized, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Deactivation_is_logical_and_preserves_requirement_record()
    {
        await using var db = TestDb.Create();
        var offering = await SeedOfferingAsync(db);
        var service = CreateService(db);
        var created = await service.CreateAsync(offering.ProgramOfferingId, 1, Request("TRANSCRIPT"), CancellationToken.None);

        var result = await service.SetActiveAsync(
            offering.ProgramOfferingId,
            created.Value!.PublicId,
            1,
            new DocumentRequirementActiveDto { IsActive = false, RowVersion = created.Value.RowVersion },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(db.ProgramOfferingDocumentRequirements.Single().IsActive);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "OfferingDocumentRequirementDeactivated");
    }

    [Fact]
    public async Task Inactive_requirement_can_be_reactivated_without_creating_a_duplicate()
    {
        await using var db = TestDb.Create();
        var offering = await SeedOfferingAsync(db);
        var service = CreateService(db);
        var created = await service.CreateAsync(offering.ProgramOfferingId, 1, Request("TRANSCRIPT"), CancellationToken.None);
        var deactivated = await service.SetActiveAsync(
            offering.ProgramOfferingId,
            created.Value!.PublicId,
            1,
            new DocumentRequirementActiveDto { IsActive = false, RowVersion = created.Value.RowVersion },
            CancellationToken.None);

        var activated = await service.SetActiveAsync(
            offering.ProgramOfferingId,
            created.Value.PublicId,
            1,
            new DocumentRequirementActiveDto { IsActive = true, RowVersion = deactivated.Value!.RowVersion },
            CancellationToken.None);

        Assert.True(deactivated.IsSuccess);
        Assert.False(deactivated.Value!.IsActive);
        Assert.True(activated.IsSuccess);
        Assert.True(activated.Value!.IsActive);
        var persisted = await db.ProgramOfferingDocumentRequirements.AsNoTracking().SingleAsync();
        Assert.True(persisted.IsActive);
        Assert.Equal("TRANSCRIPT", persisted.NormalizedDocumentCode);
        Assert.Single(db.ProgramOfferingDocumentRequirements);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "OfferingDocumentRequirementActivated");
    }

    [Fact]
    public async Task Stale_rowversion_cannot_reactivate_or_overwrite_an_inactive_requirement()
    {
        await using var db = TestDb.Create();
        var offering = await SeedOfferingAsync(db);
        var service = CreateService(db);
        var created = await service.CreateAsync(offering.ProgramOfferingId, 1, Request("TRANSCRIPT"), CancellationToken.None);
        var deactivated = await service.SetActiveAsync(
            offering.ProgramOfferingId,
            created.Value!.PublicId,
            1,
            new DocumentRequirementActiveDto { IsActive = false, RowVersion = created.Value.RowVersion },
            CancellationToken.None);
        Assert.True(deactivated.IsSuccess);
        var requirement = db.ProgramOfferingDocumentRequirements.Single();
        requirement.DisplayName = "Başka yönetici güncelledi";
        await db.SaveChangesAsync();

        var stale = await service.SetActiveAsync(
            offering.ProgramOfferingId,
            created.Value.PublicId,
            1,
            new DocumentRequirementActiveDto
            {
                IsActive = true,
                RowVersion = Convert.ToBase64String([1])
            },
            CancellationToken.None);

        Assert.False(stale.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, stale.StatusCode);
        Assert.Contains("başka bir yönetici", stale.Error, StringComparison.OrdinalIgnoreCase);
        var persisted = await db.ProgramOfferingDocumentRequirements.AsNoTracking().SingleAsync();
        Assert.False(persisted.IsActive);
        Assert.Equal("Başka yönetici güncelledi", persisted.DisplayName);
        Assert.Single(db.ProgramOfferingDocumentRequirements);
    }

    [Fact]
    public async Task Open_offering_last_active_required_requirement_cannot_be_deactivated()
    {
        await using var db = TestDb.Create();
        var offering = await SeedOfferingAsync(db);
        var service = CreateService(db);
        var created = await service.CreateAsync(offering.ProgramOfferingId, 1, Request("TRANSCRIPT"), CancellationToken.None);
        offering.IsOpen = true;
        await db.SaveChangesAsync();
        var auditCount = db.SecurityAuditLogs.Count();

        var result = await service.SetActiveAsync(
            offering.ProgramOfferingId,
            created.Value!.PublicId,
            1,
            new DocumentRequirementActiveDto { IsActive = false, RowVersion = created.Value.RowVersion },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(
            "Açık bir ilanın son aktif zorunlu belge koşulu kaldırılamaz. Önce ilanı kapatın.",
            result.Error);
        Assert.True(db.ProgramOfferingDocumentRequirements.Single().IsActive);
        Assert.Equal(auditCount, db.SecurityAuditLogs.Count());
    }

    [Fact]
    public async Task Open_offering_last_active_required_requirement_cannot_be_made_optional()
    {
        await using var db = TestDb.Create();
        var offering = await SeedOfferingAsync(db);
        var service = CreateService(db);
        var created = await service.CreateAsync(offering.ProgramOfferingId, 1, Request("TRANSCRIPT"), CancellationToken.None);
        offering.IsOpen = true;
        await db.SaveChangesAsync();

        var result = await service.UpdateAsync(
            offering.ProgramOfferingId,
            created.Value!.PublicId,
            1,
            UpdateRequest(created.Value, isRequired: false),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.True(db.ProgramOfferingDocumentRequirements.Single().IsRequired);
    }

    [Fact]
    public async Task Open_offering_requirement_can_be_deactivated_when_another_active_required_one_exists()
    {
        await using var db = TestDb.Create();
        var offering = await SeedOfferingAsync(db);
        var service = CreateService(db);
        var transcript = await service.CreateAsync(offering.ProgramOfferingId, 1, Request("TRANSCRIPT"), CancellationToken.None);
        var diploma = await service.CreateAsync(offering.ProgramOfferingId, 1, Request("DIPLOMA"), CancellationToken.None);
        offering.IsOpen = true;
        await db.SaveChangesAsync();

        var result = await service.SetActiveAsync(
            offering.ProgramOfferingId,
            transcript.Value!.PublicId,
            1,
            new DocumentRequirementActiveDto { IsActive = false, RowVersion = transcript.Value.RowVersion },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsActive);
        Assert.True(db.ProgramOfferingDocumentRequirements.Single(item => item.PublicId == diploma.Value!.PublicId).IsActive);
    }

    [Theory]
    [InlineData(OfferingEvaluationState.Finalized)]
    [InlineData(OfferingEvaluationState.Published)]
    public async Task Finalized_or_published_evaluation_offering_rejects_all_requirement_mutations_without_changes(
        OfferingEvaluationState state)
    {
        await using var db = TestDb.Create();
        var offering = await SeedOfferingAsync(db);
        var service = CreateService(db);
        var active = await service.CreateAsync(
            offering.ProgramOfferingId,
            1,
            Request("TRANSCRIPT"),
            CancellationToken.None);
        var inactive = await service.CreateAsync(
            offering.ProgramOfferingId,
            1,
            Request("DIPLOMA"),
            CancellationToken.None);
        var deactivated = await service.SetActiveAsync(
            offering.ProgramOfferingId,
            inactive.Value!.PublicId,
            1,
            new DocumentRequirementActiveDto
            {
                IsActive = false,
                RowVersion = inactive.Value.RowVersion
            },
            CancellationToken.None);
        Assert.True(active.IsSuccess);
        Assert.True(deactivated.IsSuccess);

        var finalizedAt = new DateTime(2026, 7, 18, 9, 0, 0, DateTimeKind.Utc);
        offering.UsesEvaluationWorkflow = true;
        offering.EvaluationState = state;
        offering.EvaluationFinalizedAtUtc = finalizedAt;
        offering.ResultsPublishedAtUtc = state == OfferingEvaluationState.Published
            ? finalizedAt.AddHours(1)
            : null;
        await db.SaveChangesAsync();
        var offeringUpdatedAt = offering.UpdatedAtUtc;
        var offeringRowVersion = offering.RowVersion.ToArray();
        var auditCount = await db.SecurityAuditLogs.CountAsync();

        var createResult = await service.CreateAsync(
            offering.ProgramOfferingId,
            1,
            Request("REFERENCE"),
            CancellationToken.None);
        var updateResult = await service.UpdateAsync(
            offering.ProgramOfferingId,
            active.Value!.PublicId,
            1,
            new OfferingDocumentRequirementUpdateDto
            {
                DocumentCode = active.Value.DocumentCode,
                DisplayName = "Değiştirilmiş transkript",
                Description = active.Value.Description,
                IsRequired = active.Value.IsRequired,
                AllowedContentCategory = active.Value.AllowedContentCategory,
                MaximumBytes = active.Value.MaximumBytes,
                RowVersion = active.Value.RowVersion
            },
            CancellationToken.None);
        var activateResult = await service.SetActiveAsync(
            offering.ProgramOfferingId,
            inactive.Value.PublicId,
            1,
            new DocumentRequirementActiveDto
            {
                IsActive = true,
                RowVersion = deactivated.Value!.RowVersion
            },
            CancellationToken.None);
        var deactivateResult = await service.SetActiveAsync(
            offering.ProgramOfferingId,
            active.Value.PublicId,
            1,
            new DocumentRequirementActiveDto
            {
                IsActive = false,
                RowVersion = active.Value.RowVersion
            },
            CancellationToken.None);

        foreach (var result in new[] { createResult, updateResult, activateResult, deactivateResult })
        {
            Assert.False(result.IsSuccess);
            Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
            Assert.Equal(
                "Kesinleştirilmiş veya yayımlanmış bir ilanın belge koşulları değiştirilemez.",
                result.Error);
        }

        db.ChangeTracker.Clear();
        var persistedOffering = await db.ProgramOfferings.AsNoTracking().SingleAsync();
        var persistedRequirements = await db.ProgramOfferingDocumentRequirements
            .AsNoTracking()
            .ToListAsync();
        Assert.Equal(state, persistedOffering.EvaluationState);
        Assert.True(persistedOffering.UsesEvaluationWorkflow);
        Assert.Equal(finalizedAt, persistedOffering.EvaluationFinalizedAtUtc);
        Assert.Equal(offering.ResultsPublishedAtUtc, persistedOffering.ResultsPublishedAtUtc);
        Assert.Equal(offeringUpdatedAt, persistedOffering.UpdatedAtUtc);
        Assert.Equal(offeringRowVersion, persistedOffering.RowVersion);
        Assert.Equal(2, persistedRequirements.Count);
        var persistedActive = persistedRequirements.Single(item => item.PublicId == active.Value.PublicId);
        var persistedInactive = persistedRequirements.Single(item => item.PublicId == inactive.Value.PublicId);
        Assert.Equal("Transkript", persistedActive.DisplayName);
        Assert.True(persistedActive.IsActive);
        Assert.False(persistedInactive.IsActive);
        Assert.Equal(auditCount, await db.SecurityAuditLogs.CountAsync());
    }

    [Fact]
    public async Task Configuring_evaluation_offering_keeps_all_requirement_mutations_available()
    {
        await using var db = TestDb.Create();
        var offering = await SeedOfferingAsync(db);
        offering.UsesEvaluationWorkflow = true;
        offering.EvaluationState = OfferingEvaluationState.Configuring;
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var created = await service.CreateAsync(
            offering.ProgramOfferingId,
            1,
            Request("TRANSCRIPT"),
            CancellationToken.None);
        var updated = await service.UpdateAsync(
            offering.ProgramOfferingId,
            created.Value!.PublicId,
            1,
            new OfferingDocumentRequirementUpdateDto
            {
                DocumentCode = created.Value.DocumentCode,
                DisplayName = "Güncel transkript",
                Description = created.Value.Description,
                IsRequired = created.Value.IsRequired,
                AllowedContentCategory = created.Value.AllowedContentCategory,
                MaximumBytes = created.Value.MaximumBytes,
                RowVersion = created.Value.RowVersion
            },
            CancellationToken.None);
        var deactivated = await service.SetActiveAsync(
            offering.ProgramOfferingId,
            created.Value.PublicId,
            1,
            new DocumentRequirementActiveDto
            {
                IsActive = false,
                RowVersion = updated.Value!.RowVersion
            },
            CancellationToken.None);
        var activated = await service.SetActiveAsync(
            offering.ProgramOfferingId,
            created.Value.PublicId,
            1,
            new DocumentRequirementActiveDto
            {
                IsActive = true,
                RowVersion = deactivated.Value!.RowVersion
            },
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.True(updated.IsSuccess);
        Assert.Equal("Güncel transkript", updated.Value!.DisplayName);
        Assert.True(deactivated.IsSuccess);
        Assert.False(deactivated.Value!.IsActive);
        Assert.True(activated.IsSuccess);
        Assert.True(activated.Value!.IsActive);
    }

    [Fact]
    public async Task Non_evaluation_legacy_offering_is_not_locked_by_evaluation_state()
    {
        await using var db = TestDb.Create();
        var offering = await SeedOfferingAsync(db);
        offering.UsesEvaluationWorkflow = false;
        offering.EvaluationState = OfferingEvaluationState.Published;
        await db.SaveChangesAsync();

        var result = await CreateService(db).CreateAsync(
            offering.ProgramOfferingId,
            1,
            Request("TRANSCRIPT"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(db.ProgramOfferingDocumentRequirements);
    }

    private static OfferingDocumentRequirementService CreateService(GraduateAppDbContext db) => new(
        db,
        new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)),
        Options.Create(new DocumentUploadOptions { MaximumBytes = 2048 }));

    private static OfferingDocumentRequirementCreateDto Request(string code) => new()
    {
        DocumentCode = code,
        DisplayName = "Transkript",
        IsRequired = true,
        AllowedContentCategory = DocumentContentCategory.PdfOnly,
        MaximumBytes = 1024
    };

    private static OfferingDocumentRequirementUpdateDto UpdateRequest(
        OfferingDocumentRequirementDto requirement,
        bool isRequired) => new()
        {
            DocumentCode = requirement.DocumentCode,
            DisplayName = requirement.DisplayName,
            Description = requirement.Description,
            IsRequired = isRequired,
            AllowedContentCategory = requirement.AllowedContentCategory,
            MaximumBytes = requirement.MaximumBytes,
            RowVersion = requirement.RowVersion
        };

    private static async Task<ProgramOffering> SeedOfferingAsync(GraduateAppDbContext db)
    {
        var offering = new ProgramOffering
        {
            ProgramId = 1,
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall,
            Quota = 1
        };
        db.ProgramOfferings.Add(offering);
        await db.SaveChangesAsync();
        return offering;
    }
}
