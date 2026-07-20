using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed class LoginIdentityMigrationTests
{
    private const string PreviousMigration = "20260720105222_RemoveLegacyApplicationStatusTrigger";
    private const string LoginIdentityMigration = "20260720115611_AddCentralLoginIdentities";
    private const string ValidationMigration = "20260720124735_ValidateCentralLoginIdentityData";

    [Fact]
    public void Migration_script_checks_collision_before_creating_table_and_blocks_destructive_down()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppLoginIdentityScriptTest;Integrated Security=true")
            .Options;
        using var db = new GraduateAppDbContext(options);
        var migrator = db.GetService<IMigrator>();

        var up = migrator.GenerateScript(PreviousMigration, LoginIdentityMigration);
        var down = migrator.GenerateScript(LoginIdentityMigration, PreviousMigration);

        Assert.Contains("THROW 51020", up, StringComparison.Ordinal);
        Assert.True(
            up.IndexOf("THROW 51020", StringComparison.Ordinal)
                < up.IndexOf("CREATE TABLE [LoginIdentities]", StringComparison.Ordinal));
        Assert.Contains("INSERT INTO [dbo].[LoginIdentities]", up, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX [IX_LoginIdentities_NormalizedEmail]", up, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM [dbo].[Students]", up, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM [dbo].[Admins]", up, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("THROW 51021", down, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE [LoginIdentities]", down, StringComparison.OrdinalIgnoreCase);
    }

    [LocalDbFact]
    public async Task Cross_role_collision_stops_migration_without_changing_existing_data()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppIdentityCollision_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await CreateCurrentAuthSchemaAsync(database);
        await database.ExecuteAsync(CreateExistingAccountsSql("SHARED@EXAMPLE.TEST", "SHARED@EXAMPLE.TEST"));

        var exception = await Assert.ThrowsAsync<SqlException>(
            () => database.MigrateAsync(LoginIdentityMigration));

        Assert.Equal(51020, exception.Number);
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Students];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Admins];"));
        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                "SELECT CASE WHEN OBJECT_ID(N'[dbo].[LoginIdentities]', N'U') IS NULL THEN 0 ELSE 1 END;"));
        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'{LoginIdentityMigration}';"));
    }

    [LocalDbFact]
    public async Task Migration_backfills_each_account_and_enforces_global_email_uniqueness()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppIdentityBackfill_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await CreateCurrentAuthSchemaAsync(database);
        await database.ExecuteAsync(CreateExistingAccountsSql("STUDENT@EXAMPLE.TEST", "ADMIN@EXAMPLE.TEST"));

        await database.MigrateAsync(ValidationMigration);

        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[LoginIdentities];"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [dbo].[LoginIdentities] WHERE [AccountType] = N'Student' AND [StudentTC] IS NOT NULL AND [AdminID] IS NULL;"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [dbo].[LoginIdentities] WHERE [AccountType] = N'Admin' AND [StudentTC] IS NULL AND [AdminID] IS NOT NULL;"));
    }

    [LocalDbFact]
    public async Task Concurrent_same_email_registrations_allow_only_one_student()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppIdentityRace_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await CreateCurrentAuthSchemaAsync(database);
        await database.MigrateAsync(LoginIdentityMigration);

        await using var firstDb = CreateContext(database.ConnectionString);
        await using var secondDb = CreateContext(database.ConnectionString);
        var firstService = CreateAuthService(firstDb);
        var secondService = CreateAuthService(secondDb);
        var firstRequest = CreateRegistrationRequest("10000000078", " race@example.test ", "05321234567");
        var secondRequest = CreateRegistrationRequest("10000000214", "RACE@EXAMPLE.TEST", "05321234568");

        var results = await Task.WhenAll(
            firstService.RegisterStudentAsync(firstRequest, CancellationToken.None),
            secondService.RegisterStudentAsync(secondRequest, CancellationToken.None));

        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => !result.IsSuccess && result.StatusCode == 409);
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Students];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[LoginIdentities];"));
    }

    [Fact]
    public void Forward_validation_migration_is_non_mutating_and_reports_safe_failure_types()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppIdentityValidationScriptTest;Integrated Security=true")
            .Options;
        using var db = new GraduateAppDbContext(options);
        var script = db.GetService<IMigrator>().GenerateScript(LoginIdentityMigration, ValidationMigration);

        Assert.Contains("THROW 51031", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51032", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51033", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51034", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51035", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51036", script, StringComparison.Ordinal);
        Assert.Contains("@affectedCount", script, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE [dbo].[Students]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE [dbo].[Admins]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM [dbo].[Students]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM [dbo].[Admins]", script, StringComparison.OrdinalIgnoreCase);
    }

    [LocalDbFact]
    public async Task Canonical_normalized_email_mismatch_stops_forward_migration_without_data_changes()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppIdentityCanonical_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await CreateCurrentAuthSchemaAsync(database);
        await database.ExecuteAsync(CreateExistingAccountsSql("STUDENT@EXAMPLE.TEST", "ADMIN@EXAMPLE.TEST"));
        await database.MigrateAsync(LoginIdentityMigration);
        await database.ExecuteAsync(
            "UPDATE [dbo].[Students] SET [NormalizedEmail] = N'WRONG@EXAMPLE.TEST' WHERE [TC] = '10000000146';");

        var exception = await Assert.ThrowsAsync<SqlException>(
            () => database.MigrateAsync(ValidationMigration));

        Assert.Equal(51033, exception.Number);
        Assert.Contains("1 hesapta", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("student@example.test", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "WRONG@EXAMPLE.TEST",
            await database.ScalarAsync<string>(
                "SELECT [NormalizedEmail] FROM [dbo].[Students] WHERE [TC] = '10000000146';"));
        Assert.Equal(
            "STUDENT@EXAMPLE.TEST",
            await database.ScalarAsync<string>(
                "SELECT [NormalizedEmail] FROM [dbo].[LoginIdentities] WHERE [StudentTC] = '10000000146';"));
        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'{ValidationMigration}';"));
    }

    [LocalDbFact]
    public async Task Subject_constraints_accept_role_specific_nulls_and_reject_invalid_shapes()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppIdentitySubjects_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await CreateCurrentAuthSchemaAsync(database);
        await database.ExecuteAsync(CreateExistingAccountsSql("STUDENT@EXAMPLE.TEST", "ADMIN@EXAMPLE.TEST"));
        await database.MigrateAsync(LoginIdentityMigration);

        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [dbo].[LoginIdentities] WHERE [AccountType] = N'Student' AND [StudentTC] IS NOT NULL AND [AdminID] IS NULL;"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [dbo].[LoginIdentities] WHERE [AccountType] = N'Admin' AND [StudentTC] IS NULL AND [AdminID] IS NOT NULL;"));
        Assert.Equal(
            3,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[LoginIdentities]') AND [is_unique] = 1 AND [name] IN (N'IX_LoginIdentities_NormalizedEmail', N'IX_LoginIdentities_StudentTC', N'IX_LoginIdentities_AdminID');"));

        await database.ExecuteAsync(
            """
            INSERT INTO [dbo].[Students]
                ([TC], [PublicID], [StudentName], [StudentSurname], [Email], [NormalizedEmail], [PasswordHash], [SecurityStamp], [IsActive], [AccessFailedCount], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                ('10000000214', NEWID(), N'İkinci', N'Öğrenci', N'second@example.test', N'SECOND@EXAMPLE.TEST', 'hash', 'stamp-2', 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME());

            INSERT INTO [dbo].[Admins]
                ([Email], [NormalizedEmail], [PasswordHash], [SecurityStamp], [AccessFailedCount], [MustChangePassword], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (N'second.admin@example.test', N'SECOND.ADMIN@EXAMPLE.TEST', 'hash', 'stamp-2', 0, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            """);

        var bothSubjects = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
            """
            INSERT INTO [dbo].[LoginIdentities]
                ([NormalizedEmail], [AccountType], [StudentTC], [AdminID], [CreatedAtUtc])
            VALUES
                (N'BOTH@EXAMPLE.TEST', N'Student', '10000000214',
                 (SELECT [AdminID] FROM [dbo].[Admins] WHERE [NormalizedEmail] = N'SECOND.ADMIN@EXAMPLE.TEST'),
                 SYSUTCDATETIME());
            """));
        var neitherSubject = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
            """
            INSERT INTO [dbo].[LoginIdentities]
                ([NormalizedEmail], [AccountType], [StudentTC], [AdminID], [CreatedAtUtc])
            VALUES
                (N'NEITHER@EXAMPLE.TEST', N'Student', NULL, NULL, SYSUTCDATETIME());
            """));

        Assert.Contains("CK_LoginIdentities_Subject", bothSubjects.Message, StringComparison.Ordinal);
        Assert.Contains("CK_LoginIdentities_Subject", neitherSubject.Message, StringComparison.Ordinal);
    }

    private static GraduateAppDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<GraduateAppDbContext>().UseSqlServer(connectionString).Options);

    private static AuthService CreateAuthService(GraduateAppDbContext db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Web:BaseUrl"] = "https://localhost:7272"
            })
            .Build();
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        return new AuthService(
            db,
            new PasswordHasher<Student>(),
            new PasswordHasher<Admin>(),
            new StubAccessTokenService(),
            new CapturingEmailSender(),
            configuration,
            clock,
            NullLogger<AuthService>.Instance,
            new StudentRegistrationValidator(clock, Options.Create(new RegistrationOptions())),
            new InvariantEmailNormalizer());
    }

    private static RegisterStudentDto CreateRegistrationRequest(string tc, string email, string telephone) => new()
    {
        Tc = tc,
        FirstName = "Test",
        LastName = "Öğrenci",
        FatherName = "Test Baba",
        BirthDate = new DateOnly(2000, 1, 1),
        Email = email,
        Telephone = telephone,
        Password = "Concurrent-Password-1!",
        ConfirmPassword = "Concurrent-Password-1!"
    };

    private static Task CreateCurrentAuthSchemaAsync(LocalDbTestDatabase database) => database.ExecuteAsync(
        """
        CREATE TABLE [dbo].[Students]
        (
            [TC] char(11) NOT NULL,
            [PublicID] uniqueidentifier NOT NULL CONSTRAINT [DF_Students_PublicID] DEFAULT (NEWID()),
            [StudentName] nvarchar(50) NOT NULL,
            [StudentSurname] nvarchar(50) NOT NULL,
            [FatherName] nvarchar(50) NULL,
            [BirthDate] date NULL,
            [Email] nvarchar(254) NOT NULL,
            [NormalizedEmail] nvarchar(254) NOT NULL,
            [Telephone] varchar(15) NULL,
            [PasswordHash] varchar(512) NOT NULL,
            [SecurityStamp] varchar(64) NOT NULL,
            [IsActive] bit NOT NULL CONSTRAINT [DF_Students_IsActive] DEFAULT (1),
            [AccessFailedCount] int NOT NULL CONSTRAINT [DF_Students_AccessFailedCount] DEFAULT (0),
            [LockoutEndUtc] datetimeoffset NULL,
            [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_Students_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
            [UpdatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_Students_UpdatedAtUtc] DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT [PK_Students] PRIMARY KEY ([TC])
        );
        CREATE UNIQUE INDEX [IX_Students_PublicID] ON [dbo].[Students] ([PublicID]);
        CREATE UNIQUE INDEX [IX_Students_NormalizedEmail] ON [dbo].[Students] ([NormalizedEmail]);
        CREATE UNIQUE INDEX [IX_Students_Telephone] ON [dbo].[Students] ([Telephone]) WHERE [Telephone] IS NOT NULL;

        CREATE TABLE [dbo].[Admins]
        (
            [AdminID] int IDENTITY(1,1) NOT NULL,
            [Email] nvarchar(254) NOT NULL,
            [NormalizedEmail] nvarchar(254) NOT NULL,
            [PasswordHash] varchar(512) NOT NULL,
            [SecurityStamp] varchar(64) NOT NULL,
            [AccessFailedCount] int NOT NULL CONSTRAINT [DF_Admins_AccessFailedCount] DEFAULT (0),
            [LockoutEndUtc] datetimeoffset NULL,
            [MustChangePassword] bit NOT NULL CONSTRAINT [DF_Admins_MustChangePassword] DEFAULT (1),
            [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_Admins_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
            [UpdatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_Admins_UpdatedAtUtc] DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT [PK_Admins] PRIMARY KEY ([AdminID])
        );
        CREATE UNIQUE INDEX [IX_Admins_NormalizedEmail] ON [dbo].[Admins] ([NormalizedEmail]);

        CREATE TABLE [dbo].[__EFMigrationsHistory]
        (
            [MigrationId] nvarchar(150) NOT NULL,
            [ProductVersion] nvarchar(32) NOT NULL,
            CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
        );
        INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
        VALUES
            (N'20260717065942_HardenExistingSchema', N'10.0.10'),
            (N'20260717110647_FixNullableStudentTelephoneUniqueness', N'10.0.10'),
            (N'20260717123013_AddAcademicPeriodOfferings', N'10.0.10'),
            (N'20260720082324_AddStudentActivationAndPublicId', N'10.0.10'),
            (N'20260720105222_RemoveLegacyApplicationStatusTrigger', N'10.0.10');
        """);

    private static string CreateExistingAccountsSql(string studentEmail, string adminEmail) =>
        $"""
        INSERT INTO [dbo].[Students]
            ([TC], [PublicID], [StudentName], [StudentSurname], [Email], [NormalizedEmail], [PasswordHash], [SecurityStamp], [IsActive], [AccessFailedCount], [CreatedAtUtc], [UpdatedAtUtc])
        VALUES
            ('10000000146', NEWID(), N'Test', N'Öğrenci', N'student@example.test', N'{studentEmail}', 'hash', 'stamp', 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME());

        INSERT INTO [dbo].[Admins]
            ([Email], [NormalizedEmail], [PasswordHash], [SecurityStamp], [AccessFailedCount], [MustChangePassword], [CreatedAtUtc], [UpdatedAtUtc])
        VALUES
            (N'admin@example.test', N'{adminEmail}', 'hash', 'stamp', 0, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
        """;
}
