using Microsoft.Data.SqlClient;

namespace GraduateApp.Tests;

public sealed class SecureApplicationDocumentsMigrationIntegrationTests
{
    [LocalDbFact]
    public async Task Migration_preserves_legacy_applications_and_marks_them_outside_document_workflow()
    {
        await using var database = new LocalDbTestDatabase($"GraduateAppDocuments_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        await CreatePreMigrationSchemaAsync(database, includeStatusConstraint: true);

        await database.MigrateAsync();

        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Applications];"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT CONVERT(int, [UsesDocumentWorkflow]) FROM [dbo].[Applications];"));
        Assert.Equal("Pending", await database.ScalarAsync<string>("SELECT [CurrentStatus] FROM [dbo].[Applications];"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Applications] WHERE [PublicID] IS NULL;"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE [name] = N'ApplicationDocuments';"));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE [name] = N'CK_Applications_CurrentStatus' AND [definition] LIKE N'%Draft%';"));
    }

    [LocalDbFact]
    public async Task Migration_stops_before_changes_when_expected_status_constraint_is_missing()
    {
        await using var database = new LocalDbTestDatabase($"GraduateAppDocuments_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        await CreatePreMigrationSchemaAsync(database, includeStatusConstraint: false);

        var exception = await Assert.ThrowsAsync<SqlException>(() => database.MigrateAsync());

        Assert.Contains("durum constraint", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.Applications') AND [name] = N'PublicID';"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Applications];"));
    }

    private static async Task CreatePreMigrationSchemaAsync(
        LocalDbTestDatabase database,
        bool includeStatusConstraint)
    {
        var constraint = includeStatusConstraint
            ? ", CONSTRAINT [CK_Applications_CurrentStatus] CHECK ([CurrentStatus] IN (N'Pending',N'UnderReview',N'Approved',N'Rejected',N'Withdrawn'))"
            : string.Empty;
        await database.ExecuteAsync(
            $"""
            CREATE TABLE [dbo].[ProgramOfferings]
            (
                [ProgramOfferingID] int IDENTITY NOT NULL CONSTRAINT [PK_ProgramOfferings] PRIMARY KEY
            );
            CREATE TABLE [dbo].[Admins]
            (
                [AdminID] int IDENTITY NOT NULL CONSTRAINT [PK_Admins] PRIMARY KEY
            );
            CREATE TABLE [dbo].[SecurityAuditLogs]
            (
                [AuditId] bigint IDENTITY NOT NULL CONSTRAINT [PK_SecurityAuditLogs] PRIMARY KEY
            );
            CREATE TABLE [dbo].[Applications]
            (
                [ApplicationID] int IDENTITY NOT NULL CONSTRAINT [PK_Applications] PRIMARY KEY,
                [TC] char(11) NOT NULL,
                [ProgramOfferingID] int NOT NULL,
                [CurrentStatus] nvarchar(50) NOT NULL
                {constraint}
            );
            INSERT INTO [dbo].[ProgramOfferings] DEFAULT VALUES;
            INSERT INTO [dbo].[Applications] ([TC], [ProgramOfferingID], [CurrentStatus])
            VALUES ('10000000146', 1, N'Pending');
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
                (N'20260721120908_AddAdminAccountLifecycle', N'10.0.10');
            """);
    }
}
