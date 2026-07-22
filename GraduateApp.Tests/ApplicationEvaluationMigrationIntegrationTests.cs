using GraduateApp.API.Domain;
using GraduateApp.API.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.Tests;

public sealed class ApplicationEvaluationMigrationIntegrationTests
{
    [LocalDbFact]
    public async Task Migration_preserves_existing_rows_as_legacy_and_adds_evaluation_schema()
    {
        await using var database = new LocalDbTestDatabase($"GraduateAppEvaluation_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        await SeedLegacyRowsAsync(database.ConnectionString);
        await RemoveEvaluationSchemaAndMarkPreviousMigrationsAppliedAsync(database);

        await database.MigrateAsync();

        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Applications];"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT CONVERT(int, [UsesEvaluationWorkflow]) FROM [dbo].[Applications];"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT CONVERT(int, [UsesEvaluationWorkflow]) FROM [dbo].[ProgramOfferings];"));
        Assert.Equal("Pending", await database.ScalarAsync<string>("SELECT [CurrentStatus] FROM [dbo].[Applications];"));
        Assert.Equal("Configuring", await database.ScalarAsync<string>("SELECT [EvaluationState] FROM [dbo].[ProgramOfferings];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE [name] = N'ApplicationEvaluations';"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE [name] = N'ApplicationEvaluationComponents';"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE [name] = N'ProgramOfferingEvaluationCriteria';"));
        Assert.Equal(5, await database.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.indexes WHERE [name] IN
            (
                N'IX_ApplicationEvaluations_ApplicationID',
                N'IX_ApplicationEvaluations_ProgramOfferingID_Rank',
                N'IX_ProgramOfferingEvaluationCriteria_ProgramOfferingID_NormalizedCode',
                N'IX_ProgramOfferingEvaluationCriteria_ProgramOfferingID_ExamID',
                N'IX_ProgramOfferingEvaluationCriteria_ProgramOfferingID_TieBreakPriority'
            );
            """));
        Assert.True(await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE [name] LIKE N'CK_ApplicationEvaluation%' OR [name] LIKE N'CK_ProgramOfferings_Evaluation%';") >= 10);
        Assert.Equal(3, await database.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.columns
            WHERE [object_id] IN
                (OBJECT_ID(N'dbo.ApplicationEvaluations'), OBJECT_ID(N'dbo.ApplicationEvaluationComponents'), OBJECT_ID(N'dbo.ProgramOfferingEvaluationCriteria'))
              AND [name] = N'RowVersion'
              AND [system_type_id] = TYPE_ID(N'timestamp');
            """));
    }

    [LocalDbFact]
    public async Task Migration_fails_before_changes_when_secure_base_schema_is_missing()
    {
        await using var database = new LocalDbTestDatabase($"GraduateAppEvaluationGuard_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        await CreatePreviousHistoryAsync(database);

        var exception = await Assert.ThrowsAsync<SqlException>(() => database.MigrateAsync());

        Assert.Contains("güvenli temel şema", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE [name] = N'ApplicationEvaluations';"));
    }

    private static async Task SeedLegacyRowsAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var db = new GraduateAppDbContext(options);
        var student = new Student
        {
            Tc = "10000000146",
            PublicId = Guid.NewGuid(),
            StudentName = "Legacy",
            StudentSurname = "Aday",
            BirthDate = new DateOnly(1995, 1, 1),
            Email = "legacy@example.test",
            NormalizedEmail = "LEGACY@EXAMPLE.TEST",
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var offering = new ProgramOffering
        {
            Program = new GraduateApp.API.Models.Program
            {
                ProgramName = "Legacy Program",
                DegreeType = "Tezli Yüksek Lisans",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Institute = new Institute
                {
                    InstituteName = "Legacy Enstitü",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                }
            },
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall,
            Quota = 1,
            IsOpen = false,
            UsesEvaluationWorkflow = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.AddRange(student, offering);
        db.Applications.Add(new Application
        {
            PublicId = Guid.NewGuid(),
            TcNavigation = student,
            ProgramOffering = offering,
            ApplicationDate = DateTime.UtcNow,
            CurrentStatus = ApplicationStatus.Pending.ToString(),
            UsesDocumentWorkflow = false,
            UsesEvaluationWorkflow = false
        });
        await db.SaveChangesAsync();
    }

    private static async Task RemoveEvaluationSchemaAndMarkPreviousMigrationsAppliedAsync(LocalDbTestDatabase database)
    {
        await database.ExecuteAsync(
            """
            DROP TABLE [dbo].[ApplicationEvaluationComponents];
            DROP TABLE [dbo].[ApplicationEvaluations];
            DROP TABLE [dbo].[ProgramOfferingEvaluationCriteria];
            ALTER TABLE [dbo].[ProgramOfferings] DROP CONSTRAINT [CK_ProgramOfferings_EvaluationState];
            ALTER TABLE [dbo].[ProgramOfferings] DROP CONSTRAINT [CK_ProgramOfferings_EvaluationLifecycle];

            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME([parent_object_id])) + N'.' +
                QUOTENAME(OBJECT_NAME([parent_object_id])) + N' DROP CONSTRAINT ' + QUOTENAME([name]) + N';'
            FROM sys.default_constraints
            WHERE [parent_object_id] IN (OBJECT_ID(N'dbo.Applications'), OBJECT_ID(N'dbo.ProgramOfferings'))
              AND COL_NAME([parent_object_id], [parent_column_id]) IN
                  (N'UsesEvaluationWorkflow', N'EvaluationState');
            EXEC sys.sp_executesql @sql;

            ALTER TABLE [dbo].[Applications] DROP COLUMN [UsesEvaluationWorkflow];
            ALTER TABLE [dbo].[ProgramOfferings] DROP COLUMN
                [EvaluationFinalizedAtUtc], [EvaluationState], [ResultsPublishedAtUtc], [UsesEvaluationWorkflow];
            """);
        await CreatePreviousHistoryAsync(database);
    }

    private static Task CreatePreviousHistoryAsync(LocalDbTestDatabase database) =>
        database.ExecuteAsync(
            """
            CREATE TABLE [dbo].[__EFMigrationsHistory]
            (
                [MigrationId] nvarchar(150) NOT NULL CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY,
                [ProductVersion] nvarchar(32) NOT NULL
            );
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES
                (N'20260717065942_HardenExistingSchema', N'10.0.10'),
                (N'20260717110647_FixNullableStudentTelephoneUniqueness', N'10.0.10'),
                (N'20260717123013_AddAcademicPeriodOfferings', N'10.0.10'),
                (N'20260720082324_AddStudentActivationAndPublicId', N'10.0.10'),
                (N'20260720105222_RemoveLegacyApplicationStatusTrigger', N'10.0.10'),
                (N'20260720115611_AddCentralLoginIdentities', N'10.0.10'),
                (N'20260720124735_ValidateCentralLoginIdentityData', N'10.0.10'),
                (N'20260721090323_AddInstituteProgramAdministration', N'10.0.10'),
                (N'20260721120908_AddAdminAccountLifecycle', N'10.0.10'),
                (N'20260722064425_SecureApplicationDocuments', N'10.0.10');
            """);
}
