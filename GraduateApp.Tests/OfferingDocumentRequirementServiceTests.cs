using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
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
