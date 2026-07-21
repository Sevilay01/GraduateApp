using Microsoft.Data.SqlClient;
using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GraduateApp.Tests;

public sealed class AcademicPeriodMigrationIntegrationTests
{
    [LocalDbFact]
    public async Task Migration_maps_every_legacy_application_to_closed_archived_offering_without_data_loss()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppOffering_{Guid.NewGuid():N}",
            databaseCollation: null);
        await database.CreateAsync();
        await CreateLegacySchemaAsync(database);

        await database.MigrateAsync("20260717123013_AddAcademicPeriodOfferings");

        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Applications];"));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[ProgramOfferings];"));
        Assert.Equal(
            2,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM [dbo].[Applications] AS a
                INNER JOIN [dbo].[ProgramOfferings] AS po
                    ON po.[ProgramOfferingID] = a.[ProgramOfferingID]
                WHERE po.[AcademicYearStart] = 0
                  AND po.[Term] = 0
                  AND po.[IsOpen] = 0
                  AND po.[IsArchived] = 1
                  AND po.[Quota] = 0;
                """));
        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'[dbo].[Applications]')
                  AND [name] = N'ProgramID';
                """));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'[dbo].[Applications]')
                  AND [name] = N'ProgramOfferingID'
                  AND [is_nullable] = 0;
                """));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[ApplicationStatusHistory];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[ReferenceLetters];"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM [dbo].[__EFMigrationsHistory]
                WHERE [MigrationId] = N'20260717123013_AddAcademicPeriodOfferings';
                """));

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
            """
            INSERT INTO [dbo].[Applications]
                ([TC], [ProgramOfferingID], [ApplicationDate], [CurrentStatus])
            SELECT N'10000000078', MIN([ProgramOfferingID]), SYSUTCDATETIME(), N'Pending'
            FROM [dbo].[ProgramOfferings]
            WHERE [ProgramID] = 1;
            """));
        Assert.Contains(duplicate.Number, new[] { 2601, 2627 });
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Applications];"));
    }

    [LocalDbFact]
    public async Task Idempotent_script_executes_without_early_reference_to_new_application_column()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppOfferingScript_{Guid.NewGuid():N}",
            databaseCollation: null);
        await database.CreateAsync();
        await CreateLegacySchemaAsync(database);
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;
        await using var dbContext = new GraduateAppDbContext(options);
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: "20260717110647_FixNullableStudentTelephoneUniqueness",
            toMigration: "20260717123013_AddAcademicPeriodOfferings",
            options: MigrationsSqlGenerationOptions.Idempotent);

        await database.ExecuteSqlServerScriptAsync(script);

        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM [dbo].[__EFMigrationsHistory]
                WHERE [MigrationId] = N'20260717123013_AddAcademicPeriodOfferings';
                """));
        Assert.Equal(
            2,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM [dbo].[Applications]
                WHERE [ProgramOfferingID] IS NOT NULL;
                """));
    }

    private static async Task CreateLegacySchemaAsync(LocalDbTestDatabase database)
    {
        await database.ExecuteAsync(
            """
            CREATE TABLE [dbo].[Institutes]
            (
                [InstituteID] int IDENTITY NOT NULL CONSTRAINT [PK_Institutes] PRIMARY KEY,
                [InstituteName] nvarchar(100) NOT NULL
            );
            CREATE TABLE [dbo].[Students]
            (
                [TC] char(11) NOT NULL CONSTRAINT [PK_Students] PRIMARY KEY,
                [StudentName] nvarchar(50) NOT NULL,
                [StudentSurname] nvarchar(50) NOT NULL,
                [Email] nvarchar(254) NOT NULL
            );
            CREATE TABLE [dbo].[Exams]
            (
                [ExamID] int IDENTITY NOT NULL CONSTRAINT [PK_Exams] PRIMARY KEY,
                [ExamName] nvarchar(50) NOT NULL
            );
            CREATE TABLE [dbo].[Universities]
            (
                [UniversityID] int IDENTITY NOT NULL CONSTRAINT [PK_Universities] PRIMARY KEY,
                [UniversityName] nvarchar(100) NOT NULL
            );
            CREATE TABLE [dbo].[Programs]
            (
                [ProgramID] int IDENTITY NOT NULL CONSTRAINT [PK_Programs] PRIMARY KEY,
                [InstituteID] int NOT NULL,
                [ProgramName] nvarchar(100) NOT NULL,
                [DegreeType] nvarchar(50) NULL,
                [IsOpen] bit NOT NULL CONSTRAINT [DF_Programs_IsOpen] DEFAULT (0),
                [ApplicationDeadlineUtc] datetime2 NULL,
                CONSTRAINT [FK_Programs_Institutes_InstituteID]
                    FOREIGN KEY ([InstituteID]) REFERENCES [dbo].[Institutes] ([InstituteID])
            );
            CREATE TABLE [dbo].[EducationInfo]
            (
                [EducationID] int IDENTITY NOT NULL CONSTRAINT [PK_EducationInfo] PRIMARY KEY,
                [TC] char(11) NOT NULL,
                [UniversityID] int NULL,
                [GNO] decimal(3,2) NULL
            );
            CREATE TABLE [dbo].[Applications]
            (
                [ApplicationID] int IDENTITY NOT NULL CONSTRAINT [PK_Applications] PRIMARY KEY,
                [TC] char(11) NOT NULL,
                [ProgramID] int NOT NULL,
                [ApplicationDate] datetime2 NOT NULL CONSTRAINT [DF_Applications_ApplicationDate] DEFAULT (SYSUTCDATETIME()),
                [CurrentStatus] nvarchar(50) NOT NULL CONSTRAINT [DF_Applications_CurrentStatus] DEFAULT (N'Pending'),
                [RowVersion] rowversion NOT NULL,
                CONSTRAINT [FK_Applications_Students_TC] FOREIGN KEY ([TC]) REFERENCES [dbo].[Students] ([TC]),
                CONSTRAINT [FK_Applications_Programs_ProgramID] FOREIGN KEY ([ProgramID]) REFERENCES [dbo].[Programs] ([ProgramID])
            );
            CREATE INDEX [IX_Applications_ProgramID] ON [dbo].[Applications] ([ProgramID]);
            CREATE UNIQUE INDEX [IX_Applications_TC_ProgramID] ON [dbo].[Applications] ([TC], [ProgramID]);
            CREATE TABLE [dbo].[ApplicationStatusHistory]
            (
                [HistoryID] int IDENTITY NOT NULL CONSTRAINT [PK_ApplicationStatusHistory] PRIMARY KEY,
                [ApplicationID] int NOT NULL,
                [StatusName] nvarchar(50) NOT NULL,
                CONSTRAINT [FK_ApplicationStatusHistory_Applications_ApplicationID]
                    FOREIGN KEY ([ApplicationID]) REFERENCES [dbo].[Applications] ([ApplicationID])
            );
            CREATE TABLE [dbo].[ReferenceLetters]
            (
                [ReferenceID] int IDENTITY NOT NULL CONSTRAINT [PK_ReferenceLetters] PRIMARY KEY,
                [ApplicationID] int NOT NULL,
                CONSTRAINT [FK_ReferenceLetters_Applications_ApplicationID]
                    FOREIGN KEY ([ApplicationID]) REFERENCES [dbo].[Applications] ([ApplicationID])
            );
            CREATE TABLE [dbo].[__EFMigrationsHistory]
            (
                [MigrationId] nvarchar(150) NOT NULL,
                [ProductVersion] nvarchar(32) NOT NULL,
                CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
            );

            INSERT INTO [dbo].[Institutes] ([InstituteName]) VALUES (N'Fen Bilimleri');
            INSERT INTO [dbo].[Students] ([TC], [StudentName], [StudentSurname], [Email]) VALUES
                ('10000000078', N'Bir', N'Öğrenci', N'one@example.test'),
                ('10000000214', N'İki', N'Öğrenci', N'two@example.test');
            INSERT INTO [dbo].[Programs]
                ([InstituteID], [ProgramName], [DegreeType], [IsOpen], [ApplicationDeadlineUtc]) VALUES
                (1, N'Bilgisayar Mühendisliği', N'Tezli', 1, '2026-08-01T00:00:00'),
                (1, N'Elektrik Mühendisliği', N'Tezli', 0, NULL);
            INSERT INTO [dbo].[Applications] ([TC], [ProgramID], [ApplicationDate], [CurrentStatus]) VALUES
                ('10000000078', 1, '2026-07-01T10:00:00', N'Pending'),
                ('10000000214', 2, '2026-07-02T10:00:00', N'Approved');
            INSERT INTO [dbo].[ApplicationStatusHistory] ([ApplicationID], [StatusName]) VALUES (1, N'Pending');
            INSERT INTO [dbo].[ReferenceLetters] ([ApplicationID]) VALUES (1);
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES
                (N'20260717065942_HardenExistingSchema', N'10.0.10'),
                (N'20260717110647_FixNullableStudentTelephoneUniqueness', N'10.0.10');
            """);

        await database.ExecuteAsync(
            """
            CREATE VIEW [dbo].[vw_AdminApplicationSummary]
            AS
            SELECT
                a.[ApplicationID] AS [BasvuruNo],
                s.[TC] AS [KimlikNo],
                CONCAT(s.[StudentName], N' ', s.[StudentSurname]) AS [AdSoyad],
                s.[Email] AS [Eposta],
                i.[InstituteName] AS [Enstitu],
                p.[ProgramName] AS [Program],
                CAST(NULL AS nvarchar(100)) AS [MezunOlduguUniversite],
                CAST(NULL AS decimal(3,2)) AS [LisansOrtalamasi],
                a.[ApplicationDate] AS [BasvuruTarihi],
                a.[CurrentStatus] AS [GuncelDurum]
            FROM [dbo].[Applications] AS a
            INNER JOIN [dbo].[Students] AS s ON s.[TC] = a.[TC]
            INNER JOIN [dbo].[Programs] AS p ON p.[ProgramID] = a.[ProgramID]
            INNER JOIN [dbo].[Institutes] AS i ON i.[InstituteID] = p.[InstituteID];
            """);
    }
}
