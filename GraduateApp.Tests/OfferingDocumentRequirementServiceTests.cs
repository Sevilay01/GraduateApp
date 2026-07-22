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
