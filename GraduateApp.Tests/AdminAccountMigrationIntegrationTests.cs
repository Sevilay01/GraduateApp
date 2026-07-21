using Microsoft.Data.SqlClient;

namespace GraduateApp.Tests;

public sealed class AdminAccountMigrationIntegrationTests
{
    private const string CurrentMigration = "20260721120908_AddAdminAccountLifecycle";

    [LocalDbFact]
    public async Task Existing_admins_and_password_reset_tokens_are_preserved_and_classified()
    {
        await using var database = await CreateLegacyDatabaseAsync("Preserve");

        await database.MigrateAsync(CurrentMigration);

        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Admins];"));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Admins] WHERE [IsActive] = 1 AND [IsInvitationPending] = 0;"));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(DISTINCT [PublicID]) FROM [dbo].[Admins] WHERE [PublicID] IS NOT NULL;"));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Admins] WHERE [RowVersion] IS NOT NULL;"));
        Assert.Equal("PasswordReset", await database.ScalarAsync<string>("SELECT TOP (1) [Purpose] FROM [dbo].[PasswordResetTokens];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Admins]') AND [name] = N'IX_Admins_PublicID' AND [is_unique] = 1;"));
        Assert.Equal(1, await database.ScalarAsync<int>($"SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'{CurrentMigration}';"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Admins]') AND [name] = N'Legacy_Admin_Email_Custom';"));
    }

    [LocalDbFact]
    public async Task Identity_mismatch_stops_before_any_schema_or_data_change()
    {
        await using var database = await CreateLegacyDatabaseAsync("Mismatch");
        await database.ExecuteAsync("UPDATE [dbo].[LoginIdentities] SET [NormalizedEmail] = N'MISMATCH@EXAMPLE.TEST' WHERE [AdminID] = 2;");

        var exception = await Assert.ThrowsAsync<SqlException>(() => database.MigrateAsync(CurrentMigration));

        Assert.Equal(51406, exception.Number);
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Admins];"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT CASE WHEN COL_LENGTH(N'dbo.Admins', N'PublicID') IS NULL THEN 0 ELSE 1 END;"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT CASE WHEN COL_LENGTH(N'dbo.PasswordResetTokens', N'Purpose') IS NULL THEN 0 ELSE 1 END;"));
        Assert.Equal(0, await database.ScalarAsync<int>($"SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'{CurrentMigration}';"));
    }

    [LocalDbFact]
    public async Task Orphan_admin_identity_stops_without_repairing_or_deleting_data()
    {
        await using var database = await CreateLegacyDatabaseAsync("Orphan");
        await database.ExecuteAsync(
            "INSERT INTO [dbo].[LoginIdentities] ([NormalizedEmail], [AccountType], [StudentTC], [AdminID]) VALUES (N'ORPHAN@EXAMPLE.TEST', N'Admin', NULL, 999);");

        var exception = await Assert.ThrowsAsync<SqlException>(() => database.MigrateAsync(CurrentMigration));

        Assert.Equal(51407, exception.Number);
        Assert.Equal(3, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[LoginIdentities];"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT CASE WHEN COL_LENGTH(N'dbo.Admins', N'IsActive') IS NULL THEN 0 ELSE 1 END;"));
    }

    [Fact]
    public void Migration_orders_prechecks_before_changes_and_blocks_data_losing_down()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot(), "GraduateApp.API", "Migrations", $"{CurrentMigration}.cs"));
        var precheck = migration.IndexOf("IF OBJECT_ID(N'[dbo].[Admins]'", StringComparison.Ordinal);
        var firstChange = migration.IndexOf("migrationBuilder.AddColumn<string>", StringComparison.Ordinal);

        Assert.True(precheck >= 0);
        Assert.True(firstChange > precheck);
        Assert.Contains("LoginIdentities", migration, StringComparison.Ordinal);
        Assert.Contains("PasswordReset", migration, StringComparison.Ordinal);
        Assert.Contains("NEWID()", migration, StringComparison.Ordinal);
        Assert.Contains("throw new NotSupportedException", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("DropIndex", migration, StringComparison.Ordinal);
    }

    private static async Task<LocalDbTestDatabase> CreateLegacyDatabaseAsync(string suffix)
    {
        var database = new LocalDbTestDatabase($"GraduateAppAdminLifecycle{suffix}_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        try
        {
            await database.ExecuteAsync(
                """
                CREATE TABLE [dbo].[Admins]
                (
                    [AdminID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Admins] PRIMARY KEY,
                    [Email] nvarchar(254) NOT NULL,
                    [NormalizedEmail] nvarchar(254) NOT NULL,
                    [PasswordHash] varchar(512) NOT NULL,
                    [SecurityStamp] varchar(64) NOT NULL
                );
                CREATE INDEX [Legacy_Admin_Email_Custom] ON [dbo].[Admins] ([Email]);

                CREATE TABLE [dbo].[LoginIdentities]
                (
                    [LoginIdentityID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LoginIdentities] PRIMARY KEY,
                    [NormalizedEmail] nvarchar(254) NOT NULL,
                    [AccountType] nvarchar(20) NOT NULL,
                    [StudentTC] char(11) NULL,
                    [AdminID] int NULL
                );

                CREATE TABLE [dbo].[PasswordResetTokens]
                (
                    [TokenID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PasswordResetTokens] PRIMARY KEY,
                    [TC] char(11) NULL,
                    [AdminID] int NULL,
                    [TokenHash] varchar(256) NOT NULL
                );

                INSERT INTO [dbo].[Admins] ([Email], [NormalizedEmail], [PasswordHash], [SecurityStamp]) VALUES
                    (N'first@example.test', N'FIRST@EXAMPLE.TEST', 'hash-one', 'stamp-one'),
                    (N'second@example.test', N'SECOND@EXAMPLE.TEST', 'hash-two', 'stamp-two');
                INSERT INTO [dbo].[LoginIdentities] ([NormalizedEmail], [AccountType], [StudentTC], [AdminID]) VALUES
                    (N'FIRST@EXAMPLE.TEST', N'Admin', NULL, 1),
                    (N'SECOND@EXAMPLE.TEST', N'Admin', NULL, 2);
                INSERT INTO [dbo].[PasswordResetTokens] ([TC], [AdminID], [TokenHash])
                    VALUES (NULL, 1, 'existing-reset-token-hash');

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
                    (N'20260721090323_AddInstituteProgramAdministration', N'10.0.10');
                """);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
