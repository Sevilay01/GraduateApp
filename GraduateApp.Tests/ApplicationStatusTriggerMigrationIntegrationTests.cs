using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GraduateApp.Tests;

public sealed class ApplicationStatusTriggerMigrationIntegrationTests
{
    private const string PreviousMigration = "20260720082324_AddStudentActivationAndPublicId";
    private const string CurrentMigration = "20260720105222_RemoveLegacyApplicationStatusTrigger";

    [Fact]
    public void Migration_drops_only_the_named_trigger_and_blocks_unsafe_down()
    {
        var source = ReadMigrationSource();

        Assert.Contains("OBJECT_ID(N'[dbo].[trg_UpdateApplicationStatus]', N'TR')", source, StringComparison.Ordinal);
        Assert.Contains("DROP TRIGGER [dbo].[trg_UpdateApplicationStatus]", source, StringComparison.Ordinal);
        Assert.Contains("THROW 52020", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DropTable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE [dbo].[Applications]", source, StringComparison.OrdinalIgnoreCase);
    }

    [LocalDbFact]
    public async Task Removing_legacy_trigger_allows_first_update_and_preserves_current_row_version()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppStatusTrigger_{Guid.NewGuid():N}",
            databaseCollation: null);
        await database.CreateAsync();
        await database.ExecuteAsync(CreateSchemaSql);
        await database.ExecuteAsync(CreateLegacyTriggerSql);

        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.triggers WHERE [object_id] = OBJECT_ID(N'dbo.trg_UpdateApplicationStatus', N'TR') AND [parent_id] = OBJECT_ID(N'dbo.ApplicationStatusHistory');"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.sql_expression_dependencies WHERE [referencing_id] = OBJECT_ID(N'dbo.trg_UpdateApplicationStatus', N'TR') AND [referenced_id] = OBJECT_ID(N'dbo.Applications');"));

        await using (var migrationContext = CreateContext(database.ConnectionString))
        {
            var script = migrationContext.GetService<IMigrator>().GenerateScript(
                fromMigration: PreviousMigration,
                toMigration: CurrentMigration,
                options: MigrationsSqlGenerationOptions.Idempotent);
            await database.ExecuteSqlServerScriptAsync(script);
            await database.ExecuteSqlServerScriptAsync(script);
        }

        await database.ExecuteAsync(
            """
            ALTER TABLE [dbo].[Applications]
                ADD [PublicID] uniqueidentifier NOT NULL CONSTRAINT [DF_TestApplications_PublicID] DEFAULT (NEWID()),
                    [UsesDocumentWorkflow] bit NOT NULL CONSTRAINT [DF_TestApplications_UsesDocumentWorkflow] DEFAULT (0);
            """);

        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.triggers WHERE [object_id] = OBJECT_ID(N'dbo.trg_UpdateApplicationStatus', N'TR');"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Applications];"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[ApplicationStatusHistory];"));

        string staleRowVersion;
        byte[] trackedRowVersion;
        await using (var updateContext = CreateContext(database.ConnectionString))
        {
            var application = await updateContext.Applications.SingleAsync();
            staleRowVersion = Convert.ToBase64String(application.RowVersion);
            var service = CreateService(updateContext);

            var result = await service.UpdateStatusAsync(
                application.PublicId,
                adminId: 1,
                new ApplicationStatusUpdateDto
                {
                    NewStatus = ApplicationStatus.UnderReview,
                    RowVersion = staleRowVersion
                },
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(ApplicationStatus.UnderReview.ToString(), application.CurrentStatus);
            trackedRowVersion = application.RowVersion.ToArray();
            Assert.NotEmpty(trackedRowVersion);
        }

        await using (var verificationContext = CreateContext(database.ConnectionString))
        {
            var application = await verificationContext.Applications.AsNoTracking().SingleAsync();
            Assert.Equal(ApplicationStatus.UnderReview.ToString(), application.CurrentStatus);
            Assert.Equal(trackedRowVersion, application.RowVersion);
            Assert.Single(await verificationContext.ApplicationStatusHistories.AsNoTracking().ToListAsync());
            Assert.Single(await verificationContext.SecurityAuditLogs.AsNoTracking().ToListAsync());
        }

        await using (var staleUpdateContext = CreateContext(database.ConnectionString))
        {
            var staleResult = await CreateService(staleUpdateContext).UpdateStatusAsync(
                publicId: (await staleUpdateContext.Applications.AsNoTracking().SingleAsync()).PublicId,
                adminId: 1,
                new ApplicationStatusUpdateDto
                {
                    NewStatus = ApplicationStatus.Approved,
                    RowVersion = staleRowVersion
                },
                CancellationToken.None);

            Assert.False(staleResult.IsSuccess);
            Assert.Equal(StatusCodes.Status409Conflict, staleResult.StatusCode);
            Assert.Contains("başka bir kullanıcı", staleResult.Error, StringComparison.Ordinal);
        }

        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[ApplicationStatusHistory];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[SecurityAuditLogs];"));
    }

    private static GraduateAppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new GraduateAppDbContext(options);
    }

    private static ApplicationService CreateService(GraduateAppDbContext dbContext) =>
        new(dbContext, new TestTimeProvider(new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero)));

    private static string ReadMigrationSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(
            repositoryRoot,
            "GraduateApp.API",
            "Migrations",
            $"{CurrentMigration}.cs"));
    }

    private const string CreateSchemaSql = """
        CREATE TABLE [dbo].[Applications]
        (
            [ApplicationID] int IDENTITY NOT NULL CONSTRAINT [PK_Applications] PRIMARY KEY,
            [TC] char(11) NOT NULL,
            [ProgramOfferingID] int NOT NULL,
            [ApplicationDate] datetime2 NOT NULL,
            [CurrentStatus] nvarchar(50) NOT NULL,
            [RowVersion] rowversion NOT NULL
        );
        CREATE TABLE [dbo].[ApplicationStatusHistory]
        (
            [HistoryID] int IDENTITY NOT NULL CONSTRAINT [PK_ApplicationStatusHistory] PRIMARY KEY,
            [ApplicationID] int NOT NULL,
            [PreviousStatus] nvarchar(50) NULL,
            [StatusName] nvarchar(50) NOT NULL,
            [ChangedByAdminID] int NULL,
            [ChangeDate] datetime2 NOT NULL,
            [Notes] nvarchar(500) NULL
        );
        CREATE TABLE [dbo].[SecurityAuditLogs]
        (
            [AuditID] bigint IDENTITY NOT NULL CONSTRAINT [PK_SecurityAuditLogs] PRIMARY KEY,
            [ActorAdminID] int NULL,
            [EventType] nvarchar(100) NOT NULL,
            [TargetType] nvarchar(100) NOT NULL,
            [TargetId] varchar(100) NOT NULL,
            [Details] nvarchar(1000) NULL,
            [CreatedAtUtc] datetime2 NOT NULL
        );
        CREATE TABLE [dbo].[__EFMigrationsHistory]
        (
            [MigrationId] nvarchar(150) NOT NULL,
            [ProductVersion] nvarchar(32) NOT NULL,
            CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
        );

        INSERT INTO [dbo].[Applications]
            ([TC], [ProgramOfferingID], [ApplicationDate], [CurrentStatus])
        VALUES ('10000000146', 1, '2026-07-20T09:00:00', N'Pending');
        INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
        VALUES (N'20260720082324_AddStudentActivationAndPublicId', N'10.0.10');
        """;

    private const string CreateLegacyTriggerSql = """
        CREATE TRIGGER [dbo].[trg_UpdateApplicationStatus]
        ON [dbo].[ApplicationStatusHistory]
        AFTER INSERT
        AS
        BEGIN
            SET NOCOUNT ON;

            UPDATE application
            SET [CurrentStatus] = insertedRow.[StatusName]
            FROM [dbo].[Applications] AS application
            INNER JOIN inserted AS insertedRow
                ON insertedRow.[ApplicationID] = application.[ApplicationID];
        END;
        """;
}
