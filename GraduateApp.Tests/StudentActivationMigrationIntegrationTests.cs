using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GraduateApp.Tests;

public sealed class StudentActivationMigrationIntegrationTests
{
    private const string PreviousMigration = "20260717123013_AddAcademicPeriodOfferings";
    private const string CurrentMigration = "20260720082324_AddStudentActivationAndPublicId";

    [LocalDbFact]
    public async Task Idempotent_migration_preserves_existing_students_and_assigns_unique_public_ids()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppMigration_{Guid.NewGuid():N}",
            databaseCollation: null);
        await database.CreateAsync();
        await database.ExecuteAsync(
            $"""
            CREATE TABLE [dbo].[Students]
            (
                [TC] char(11) NOT NULL CONSTRAINT [PK_Students] PRIMARY KEY
            );
            CREATE TABLE [dbo].[__EFMigrationsHistory]
            (
                [MigrationId] nvarchar(150) NOT NULL,
                [ProductVersion] nvarchar(32) NOT NULL,
                CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
            );
            INSERT INTO [dbo].[Students] ([TC]) VALUES ('10000000146'), ('10000000214');
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES (N'{PreviousMigration}', N'10.0.10');
            """);

        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;
        await using var dbContext = new GraduateAppDbContext(options);
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: PreviousMigration,
            toMigration: CurrentMigration,
            options: MigrationsSqlGenerationOptions.Idempotent);

        await database.ExecuteSqlServerScriptAsync(script);
        await database.ExecuteSqlServerScriptAsync(script);

        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Students];"));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Students] WHERE [IsActive] = 1;"));
        Assert.Equal(
            2,
            await database.ScalarAsync<int>(
                "SELECT COUNT(DISTINCT [PublicID]) FROM [dbo].[Students] WHERE [PublicID] <> '00000000-0000-0000-0000-000000000000';"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'{CurrentMigration}';"));

        await database.ExecuteAsync("INSERT INTO [dbo].[Students] ([TC]) VALUES ('10000000078');");
        Assert.Equal(3, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Students] WHERE [IsActive] = 1;"));
        Assert.Equal(3, await database.ScalarAsync<int>("SELECT COUNT(DISTINCT [PublicID]) FROM [dbo].[Students];"));
    }
}
