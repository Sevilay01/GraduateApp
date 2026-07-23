using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.Tests;

public sealed class ApplicationEvaluationConcurrencyIntegrationTests
{
    [LocalDbFact]
    public async Task Finalization_and_publication_run_atomically_on_sql_server()
    {
        await using var database = new LocalDbTestDatabase($"GraduateAppEvaluationPublish_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var seeded = await SeedAsync(database.ConnectionString, readyForFinalization: true);

        await using (var finalizationDb = Context(database.ConnectionString))
        {
            var result = await new ApplicationEvaluationService(
                finalizationDb,
                new TestTimeProvider(new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)))
                .FinalizeAsync(
                    seeded.OfferingId,
                    seeded.AdminId,
                    new OfferingEvaluationCommandDto
                    {
                        RowVersion = Convert.ToBase64String(seeded.OfferingRowVersion)
                    },
                    CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using (var publicationDb = Context(database.ConnectionString))
        {
            var offeringRowVersion = await publicationDb.ProgramOfferings.AsNoTracking()
                .Where(item => item.ProgramOfferingId == seeded.OfferingId)
                .Select(item => item.RowVersion)
                .SingleAsync();
            var result = await new ApplicationEvaluationService(
                publicationDb,
                new TestTimeProvider(new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero)))
                .PublishAsync(
                    seeded.OfferingId,
                    seeded.AdminId,
                    new OfferingEvaluationCommandDto
                    {
                        RowVersion = Convert.ToBase64String(offeringRowVersion)
                    },
                    CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using var verification = Context(database.ConnectionString);
        Assert.Equal(
            OfferingEvaluationState.Published,
            (await verification.ProgramOfferings.AsNoTracking().SingleAsync()).EvaluationState);
        Assert.Equal(
            ApplicationStatus.Approved.ToString(),
            (await verification.Applications.AsNoTracking().SingleAsync()).CurrentStatus);
        Assert.Equal(EvaluationOutcome.Admitted, (await verification.ApplicationEvaluations.AsNoTracking().SingleAsync()).Outcome);
        Assert.Single(await verification.ApplicationStatusHistories.AsNoTracking().ToListAsync());
        Assert.Single((await verification.SecurityAuditLogs.AsNoTracking().ToListAsync()), item =>
            item.EventType == "OfferingEvaluationFinalized");
        Assert.Single((await verification.SecurityAuditLogs.AsNoTracking().ToListAsync()), item =>
            item.EventType == "OfferingResultsPublished");
    }

    [LocalDbFact]
    public async Task Concurrent_finalization_allows_exactly_one_success()
    {
        await using var database = new LocalDbTestDatabase($"GraduateAppEvaluationFinalizeRace_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var seeded = await SeedAsync(database.ConnectionString, readyForFinalization: true);

        async Task<ServiceResult> FinalizeAsync()
        {
            await using var db = Context(database.ConnectionString);
            return await new ApplicationEvaluationService(
                db,
                new TestTimeProvider(new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)))
                .FinalizeAsync(
                    seeded.OfferingId,
                    seeded.AdminId,
                    new OfferingEvaluationCommandDto
                    {
                        RowVersion = Convert.ToBase64String(seeded.OfferingRowVersion)
                    },
                    CancellationToken.None);
        }

        var results = await Task.WhenAll(FinalizeAsync(), FinalizeAsync());

        Assert.Single(results, item => item.IsSuccess);
        var conflict = Assert.Single(results, item => !item.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(
            "Değerlendirme veya ilan başka bir yönetici tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
            conflict.Error);
        Assert.DoesNotContain("deadlock", conflict.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1205", conflict.Error, StringComparison.Ordinal);
        await using var verification = Context(database.ConnectionString);
        Assert.Equal(
            OfferingEvaluationState.Finalized,
            (await verification.ProgramOfferings.AsNoTracking().SingleAsync()).EvaluationState);
        Assert.Single((await verification.SecurityAuditLogs.AsNoTracking().ToListAsync()), item =>
            item.EventType == "OfferingEvaluationFinalized");
        var evaluation = await verification.ApplicationEvaluations.AsNoTracking().SingleAsync();
        Assert.Equal(1, evaluation.Rank);
        Assert.Equal(EvaluationOutcome.Admitted, evaluation.Outcome);
        Assert.NotNull(evaluation.FinalizedAtUtc);
    }

    [LocalDbFact]
    public async Task Concurrent_evaluation_submissions_are_not_rejected_by_quota()
    {
        await using var database = new LocalDbTestDatabase($"GraduateAppEvaluationSubmitRace_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var drafts = await SeedConcurrentSubmissionsAsync(database.ConnectionString);

        async Task<ServiceResult> SubmitAsync(string tc, Guid publicId)
        {
            await using var db = Context(database.ConnectionString);
            return await new ApplicationService(
                db,
                new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)))
                .SubmitAsync(tc, publicId, CancellationToken.None);
        }

        var results = await Task.WhenAll(drafts.Select(item => SubmitAsync(item.Tc, item.PublicId)));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error));
        await using var verification = Context(database.ConnectionString);
        Assert.Equal(2, await verification.Applications.CountAsync(item => item.CurrentStatus == "Pending"));
        Assert.Equal(2, await verification.ApplicationEvaluations.CountAsync());
        Assert.Equal(2, await verification.SecurityAuditLogs.CountAsync(item => item.EventType == "ApplicationSubmitted"));
    }

    [LocalDbFact]
    public async Task Stale_offering_row_version_blocks_finalization_without_partial_results_or_audit()
    {
        await using var database = new LocalDbTestDatabase($"GraduateAppEvaluationOfferingConcurrency_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var seeded = await SeedAsync(database.ConnectionString, readyForFinalization: true);

        await using (var concurrent = Context(database.ConnectionString))
        {
            var offering = await concurrent.ProgramOfferings.SingleAsync();
            offering.UpdatedAtUtc = offering.UpdatedAtUtc.AddMinutes(1);
            await concurrent.SaveChangesAsync();
        }

        await using (var stale = Context(database.ConnectionString))
        {
            var result = await new ApplicationEvaluationService(
                stale,
                new TestTimeProvider(new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)))
                .FinalizeAsync(
                    seeded.OfferingId,
                    seeded.AdminId,
                    new OfferingEvaluationCommandDto
                    {
                        RowVersion = Convert.ToBase64String(seeded.OfferingRowVersion)
                    },
                    CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
            Assert.Equal(
                "Değerlendirme veya ilan başka bir yönetici tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                result.Error);
        }

        await using var verification = Context(database.ConnectionString);
        Assert.Equal(
            OfferingEvaluationState.Configuring,
            (await verification.ProgramOfferings.AsNoTracking().SingleAsync()).EvaluationState);
        var evaluation = await verification.ApplicationEvaluations.AsNoTracking().SingleAsync();
        Assert.Null(evaluation.Rank);
        Assert.Null(evaluation.Outcome);
        Assert.DoesNotContain(
            await verification.SecurityAuditLogs.AsNoTracking().ToListAsync(),
            item => item.EventType == "OfferingEvaluationFinalized");
    }

    [LocalDbFact]
    public async Task Stale_manual_component_row_version_returns_safe_conflict_without_partial_score_or_audit()
    {
        await using var database = new LocalDbTestDatabase($"GraduateAppEvaluationConcurrency_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var seeded = await SeedAsync(database.ConnectionString);

        await using (var concurrent = Context(database.ConnectionString))
        {
            var component = await concurrent.ApplicationEvaluationComponents.SingleAsync();
            component.RawScore = 10m;
            component.NormalizedScore = 10m;
            component.WeightedScore = 10m;
            component.ManualScoredByAdminId = seeded.AdminId;
            component.ManualScoredAtUtc = new DateTime(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc);
            await concurrent.SaveChangesAsync();
        }

        await using (var stale = Context(database.ConnectionString))
        {
            var service = new ApplicationEvaluationService(
                stale,
                new TestTimeProvider(new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)));
            var result = await service.SetManualScoreAsync(
                seeded.ApplicationPublicId,
                seeded.CriterionPublicId,
                seeded.AdminId,
                new ManualEvaluationScoreDto
                {
                    RawScore = 90m,
                    RowVersion = Convert.ToBase64String(seeded.ComponentRowVersion)
                },
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
            Assert.Contains("başka bir yönetici", result.Error, StringComparison.Ordinal);
        }

        await using var verification = Context(database.ConnectionString);
        Assert.Equal(10m, (await verification.ApplicationEvaluationComponents.AsNoTracking().SingleAsync()).RawScore);
        Assert.DoesNotContain(
            await verification.SecurityAuditLogs.AsNoTracking().ToListAsync(),
            item => item.EventType == "ApplicationManualScoreUpdated");
    }

    private static GraduateAppDbContext Context(string connectionString) => new(
        new DbContextOptionsBuilder<GraduateAppDbContext>().UseSqlServer(connectionString).Options);

    private static async Task<IReadOnlyList<SubmitDraft>> SeedConcurrentSubmissionsAsync(string connectionString)
    {
        await using var db = Context(connectionString);
        var now = new DateTime(2026, 7, 17, 9, 0, 0, DateTimeKind.Utc);
        var criterion = new ProgramOfferingEvaluationCriterion
        {
            PublicId = Guid.NewGuid(),
            Code = "GPA",
            NormalizedCode = "GPA",
            DisplayName = "Lisans GNO",
            SourceType = EvaluationCriterionSourceType.UndergraduateGpa,
            WeightBasisPoints = 10000,
            MaximumRawScore = 4m,
            TieBreakPriority = 1,
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
            ApplicationStartUtc = now.AddDays(-1),
            ApplicationDeadlineUtc = now.AddDays(1),
            Quota = 1,
            IsOpen = true,
            UsesEvaluationWorkflow = true,
            EvaluationState = OfferingEvaluationState.Configuring,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            EvaluationCriteria = [criterion],
            DocumentRequirements = [requirement]
        };
        var university = new University { UniversityName = "Test Üniversitesi" };
        var drafts = new List<SubmitDraft>();
        foreach (var (tc, suffix) in new[] { ("10000000146", "one"), ("10000000154", "two") })
        {
            var student = new Student
            {
                Tc = tc,
                PublicId = Guid.NewGuid(),
                StudentName = "Test",
                StudentSurname = "Aday",
                Email = $"{suffix}@example.test",
                NormalizedEmail = $"{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
                PasswordHash = "hash",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                EducationInfos =
                [
                    new EducationInfo
                    {
                        University = university,
                        Gno = 3.5m
                    }
                ]
            };
            var publicId = Guid.NewGuid();
            var snapshot = new ApplicationDocumentRequirementSnapshot
            {
                PublicId = Guid.NewGuid(),
                SourceRequirement = requirement,
                DocumentCode = requirement.NormalizedDocumentCode,
                DisplayName = requirement.DisplayName,
                IsRequired = true,
                AllowedContentCategory = DocumentContentCategory.PdfOnly,
                MaximumBytes = requirement.MaximumBytes
            };
            var document = new ApplicationDocument
            {
                PublicId = Guid.NewGuid(),
                RequirementSnapshot = snapshot,
                VersionNumber = 1,
                IsCurrent = true,
                OriginalFileName = "transkript.pdf",
                ObjectKey = Guid.NewGuid().ToString("N"),
                VerifiedContentType = "application/pdf",
                FileSize = 100,
                Sha256 = new string('0', 64),
                ReviewStatus = DocumentReviewStatus.Pending,
                UploadedAtUtc = now
            };
            var application = new Application
            {
                PublicId = publicId,
                Tc = tc,
                TcNavigation = student,
                ProgramOffering = offering,
                ApplicationDate = now,
                CurrentStatus = ApplicationStatus.Draft.ToString(),
                UsesDocumentWorkflow = true,
                UsesEvaluationWorkflow = true,
                DocumentRequirementSnapshots = [snapshot],
                Documents = [document]
            };
            db.Applications.Add(application);
            drafts.Add(new SubmitDraft(tc, publicId));
        }

        await db.SaveChangesAsync();
        return drafts;
    }

    private static async Task<SeededState> SeedAsync(
        string connectionString,
        bool readyForFinalization = false)
    {
        await using var db = Context(connectionString);
        var now = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        var admin = new Admin
        {
            Email = "admin@example.test",
            NormalizedEmail = "ADMIN@EXAMPLE.TEST",
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var criterion = new ProgramOfferingEvaluationCriterion
        {
            PublicId = Guid.NewGuid(),
            Code = "MANUAL",
            NormalizedCode = "MANUAL",
            DisplayName = "Mülakat",
            SourceType = EvaluationCriterionSourceType.ManualScore,
            WeightBasisPoints = 10000,
            MaximumRawScore = 100m,
            TieBreakPriority = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
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
            ApplicationDeadlineUtc = now.AddDays(-1),
            Quota = 1,
            IsOpen = false,
            UsesEvaluationWorkflow = true,
            EvaluationState = OfferingEvaluationState.Configuring,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            EvaluationCriteria = [criterion]
        };
        var student = new Student
        {
            Tc = "10000000146",
            PublicId = Guid.NewGuid(),
            StudentName = "Test",
            StudentSurname = "Aday",
            Email = "student@example.test",
            NormalizedEmail = "STUDENT@EXAMPLE.TEST",
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var component = new ApplicationEvaluationComponent
        {
            SourceCriterion = criterion,
            CriterionPublicIdSnapshot = criterion.PublicId,
            CodeSnapshot = criterion.Code,
            DisplayNameSnapshot = criterion.DisplayName,
            SourceTypeSnapshot = criterion.SourceType,
            MaximumRawScoreSnapshot = 100m,
            WeightBasisPointsSnapshot = 10000,
            TieBreakPrioritySnapshot = 1,
            RawScore = readyForFinalization ? 90m : null,
            NormalizedScore = readyForFinalization ? 90m : null,
            WeightedScore = readyForFinalization ? 90m : null,
            ManualScoredByAdmin = readyForFinalization ? admin : null,
            ManualScoredAtUtc = readyForFinalization ? now : null
        };
        var application = new Application
        {
            PublicId = Guid.NewGuid(),
            TcNavigation = student,
            ProgramOffering = offering,
            ApplicationDate = now,
            CurrentStatus = ApplicationStatus.UnderReview.ToString(),
            UsesDocumentWorkflow = true,
            UsesEvaluationWorkflow = true,
            Evaluation = new ApplicationEvaluation
            {
                ProgramOffering = offering,
                EligibilityStatus = readyForFinalization
                    ? EvaluationEligibilityStatus.Eligible
                    : EvaluationEligibilityStatus.Pending,
                TotalScore = readyForFinalization ? 90m : null,
                EligibilityDecidedByAdmin = readyForFinalization ? admin : null,
                EligibilityDecidedAtUtc = readyForFinalization ? now : null,
                Components = [component]
            }
        };
        db.AddRange(admin, application);
        await db.SaveChangesAsync();
        return new SeededState(
            admin.AdminId,
            offering.ProgramOfferingId,
            application.PublicId,
            criterion.PublicId,
            component.RowVersion.ToArray(),
            offering.RowVersion.ToArray());
    }

    private sealed record SeededState(
        int AdminId,
        int OfferingId,
        Guid ApplicationPublicId,
        Guid CriterionPublicId,
        byte[] ComponentRowVersion,
        byte[] OfferingRowVersion);

    private sealed record SubmitDraft(string Tc, Guid PublicId);
}
