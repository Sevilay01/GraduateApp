using GraduateApp.API.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GraduateApp.Tests;

public sealed class InstituteProgramMigrationIntegrationTests
{
    private const string PreviousMigration = "20260720124735_ValidateCentralLoginIdentityData";
    private const string CurrentMigration = "20260721090323_AddInstituteProgramAdministration";

    [Fact]
    public void Forward_script_runs_prechecks_before_schema_changes_and_blocks_destructive_down()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppInstituteProgramScriptTest;Integrated Security=true")
            .Options;
        using var db = new GraduateAppDbContext(options);
        var migrator = db.GetService<IMigrator>();

        var up = migrator.GenerateScript(PreviousMigration, CurrentMigration);

        Assert.Contains("THROW 51303", up, StringComparison.Ordinal);
        Assert.Contains("THROW 51306", up, StringComparison.Ordinal);
        Assert.Contains("THROW 51307", up, StringComparison.Ordinal);
        Assert.Contains("IF EXISTS", up, StringComparison.Ordinal);
        Assert.Contains("[name] = N'IX_Programs_InstituteID'", up, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX [IX_Programs_InstituteID] ON [dbo].[Programs]", up, StringComparison.Ordinal);
        Assert.True(
            up.IndexOf("THROW 51307", StringComparison.Ordinal)
                < up.IndexOf("IF EXISTS", StringComparison.Ordinal));
        Assert.True(
            up.IndexOf("IF EXISTS", StringComparison.Ordinal)
                < up.IndexOf("DROP INDEX [IX_Programs_InstituteID] ON [dbo].[Programs]", StringComparison.Ordinal));
        Assert.DoesNotContain("UPDATE [dbo].[Institutes]", up, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE [dbo].[Programs]", up, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM [dbo].[Institutes]", up, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM [dbo].[Programs]", up, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<NotSupportedException>(() => migrator.GenerateScript(CurrentMigration, PreviousMigration));
    }

    [LocalDbFact]
    public async Task In_place_upgrade_with_legacy_index_removes_it_and_preserves_rows()
    {
        await using var database = await CreateLegacyCatalogDatabaseAsync("Upgrade");
        await database.ExecuteAsync(
            """
            INSERT INTO [dbo].[Institutes] ([InstituteName]) VALUES (N'Fen Bilimleri');
            DECLARE @InstituteID int = SCOPE_IDENTITY();
            INSERT INTO [dbo].[Programs] ([InstituteID], [ProgramName], [DegreeType], [IsActive]) VALUES
                (@InstituteID, N'Bilgisayar Mühendisliği', N'Tezli Yüksek Lisans', 1),
                (@InstituteID, N'Bilgisayar Mühendisliği', N'Doktora', 1);
            """);

        await database.MigrateAsync(CurrentMigration);

        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Institutes];"));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Programs];"));
        Assert.Equal(
            "Bilgisayar Mühendisliği",
            await database.ScalarAsync<string>(
                "SELECT TOP (1) [ProgramName] FROM [dbo].[Programs] ORDER BY [ProgramID];"));
        Assert.Equal(
            2,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [dbo].[Programs] WHERE [CreatedAtUtc] IS NOT NULL AND [UpdatedAtUtc] IS NOT NULL AND [RowVersion] IS NOT NULL;"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [dbo].[Institutes] WHERE [IsActive] = 1 AND [CreatedAtUtc] IS NOT NULL AND [UpdatedAtUtc] IS NOT NULL AND [RowVersion] IS NOT NULL;"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Programs]') AND [name] = N'IX_Programs_InstituteID_ProgramName_DegreeType' AND [is_unique] = 1;"));
        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Programs]') AND [name] = N'IX_Programs_InstituteID';"));
        Assert.Equal(
            3,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.check_constraints WHERE [parent_object_id] IN (OBJECT_ID(N'[dbo].[Institutes]'), OBJECT_ID(N'[dbo].[Programs]')) AND [name] IN (N'CK_Institutes_InstituteName_Trimmed', N'CK_Programs_ProgramName_Trimmed', N'CK_Programs_DegreeType');"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'{CurrentMigration}';"));
    }

    [LocalDbFact]
    public async Task In_place_upgrade_without_legacy_index_still_succeeds()
    {
        await using var database = await CreateLegacyCatalogDatabaseAsync("NoLegacyIndex", includeLegacyIndex: false);
        await database.ExecuteAsync(
            """
            INSERT INTO [dbo].[Institutes] ([InstituteName]) VALUES (N'Sosyal Bilimler');
            DECLARE @InstituteID int = SCOPE_IDENTITY();
            INSERT INTO [dbo].[Programs] ([InstituteID], [ProgramName], [DegreeType], [IsActive])
            VALUES (@InstituteID, N'Tarih', N'Doktora', 1);
            """);

        await database.MigrateAsync(CurrentMigration);

        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Programs];"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Programs]') AND [name] = N'IX_Programs_InstituteID_ProgramName_DegreeType' AND [is_unique] = 1;"));
        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Programs]') AND [name] = N'IX_Programs_InstituteID';"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'{CurrentMigration}';"));
    }

    [LocalDbFact]
    public async Task Duplicate_program_combinations_stop_before_any_schema_or_data_change()
    {
        await using var database = await CreateLegacyCatalogDatabaseAsync("Duplicates");
        await database.ExecuteAsync(
            """
            INSERT INTO [dbo].[Institutes] ([InstituteName]) VALUES (N'Fen Bilimleri');
            DECLARE @InstituteID int = SCOPE_IDENTITY();
            INSERT INTO [dbo].[Programs] ([InstituteID], [ProgramName], [DegreeType], [IsActive]) VALUES
                (@InstituteID, N'Kimya', N'Doktora', 1),
                (@InstituteID, N'Kimya', N'Doktora', 0);
            """);

        var exception = await Assert.ThrowsAsync<SqlException>(() => database.MigrateAsync(CurrentMigration));

        Assert.Equal(51306, exception.Number);
        Assert.Contains("mükerrer", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Programs];"));
        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                "SELECT CASE WHEN COL_LENGTH(N'dbo.Institutes', N'IsActive') IS NULL THEN 0 ELSE 1 END;"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Programs]') AND [name] = N'IX_Programs_InstituteID';"));
        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'{CurrentMigration}';"));
    }

    [LocalDbFact]
    public async Task Concurrent_same_program_key_allows_only_one_database_row()
    {
        await using var database = await CreateLegacyCatalogDatabaseAsync("Race");
        await database.ExecuteAsync("INSERT INTO [dbo].[Institutes] ([InstituteName]) VALUES (N'Fen Bilimleri');");
        await database.MigrateAsync(CurrentMigration);

        var results = await Task.WhenAll(
            TryInsertProgramAsync(database.ConnectionString),
            TryInsertProgramAsync(database.ConnectionString));

        Assert.Single(results, exception => exception is null);
        var collision = Assert.Single(results, exception => exception is SqlException);
        Assert.Contains(((SqlException)collision!).Number, new[] { 2601, 2627 });
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [dbo].[Programs] WHERE [ProgramName] = N'Fizik' AND [DegreeType] = N'Doktora';"));
    }

    private static async Task<Exception?> TryInsertProgramAsync(string connectionString)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO [dbo].[Programs] ([InstituteID], [ProgramName], [DegreeType], [IsActive])
                SELECT TOP (1) [InstituteID], N'Fizik', N'Doktora', 1
                FROM [dbo].[Institutes]
                ORDER BY [InstituteID];
                """;
            await command.ExecuteNonQueryAsync();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<LocalDbTestDatabase> CreateLegacyCatalogDatabaseAsync(
        string suffix,
        bool includeLegacyIndex = true)
    {
        var database = new LocalDbTestDatabase(
            $"GraduateAppInstituteProgram{suffix}_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        try
        {
            await database.ExecuteAsync(
                """
                CREATE TABLE [dbo].[Institutes]
                (
                    [InstituteID] int IDENTITY(1,1) NOT NULL,
                    [InstituteName] nvarchar(100) NOT NULL,
                    CONSTRAINT [PK_Institutes] PRIMARY KEY ([InstituteID])
                );
                CREATE UNIQUE INDEX [IX_Institutes_InstituteName]
                    ON [dbo].[Institutes] ([InstituteName]);

                CREATE TABLE [dbo].[Programs]
                (
                    [ProgramID] int IDENTITY(1,1) NOT NULL,
                    [InstituteID] int NOT NULL,
                    [ProgramName] nvarchar(100) NOT NULL,
                    [DegreeType] nvarchar(50) NULL,
                    [IsActive] bit NOT NULL CONSTRAINT [DF_Programs_IsActive] DEFAULT (1),
                    CONSTRAINT [PK_Programs] PRIMARY KEY ([ProgramID]),
                    CONSTRAINT [FK_Programs_Institutes_InstituteID]
                        FOREIGN KEY ([InstituteID]) REFERENCES [dbo].[Institutes] ([InstituteID])
                );
                CREATE TABLE [dbo].[__EFMigrationsHistory]
                (
                    [MigrationId] nvarchar(150) NOT NULL,
                    [ProductVersion] nvarchar(32) NOT NULL,
                    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                );
                INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES
                    (N'20260717065942_HardenExistingSchema', N'10.0.10'),
                    (N'20260717110647_FixNullableStudentTelephoneUniqueness', N'10.0.10'),
                    (N'20260717123013_AddAcademicPeriodOfferings', N'10.0.10'),
                    (N'20260720082324_AddStudentActivationAndPublicId', N'10.0.10'),
                    (N'20260720105222_RemoveLegacyApplicationStatusTrigger', N'10.0.10'),
                    (N'20260720115611_AddCentralLoginIdentities', N'10.0.10'),
                    (N'20260720124735_ValidateCentralLoginIdentityData', N'10.0.10');
                """);
            if (includeLegacyIndex)
            {
                await database.ExecuteAsync(
                    "CREATE INDEX [IX_Programs_InstituteID] ON [dbo].[Programs] ([InstituteID]);");
            }

            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }
}
