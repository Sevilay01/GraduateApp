namespace GraduateApp.Tests;

public sealed class StudentResetScriptTests
{
    [Fact]
    public void Reset_script_is_transactional_and_does_not_delete_catalog_tables()
    {
        var script = ReadScript();

        Assert.Contains("BEGIN TRANSACTION", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ROLLBACK TRANSACTION", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DB_NAME()", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET XACT_ABORT ON", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DELETE FROM [dbo].[Students]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DELETE FROM [dbo].[Applications]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DELETE FROM [dbo].[StudentExamScores]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DELETE FROM [dbo].[SecurityAuditLogs]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM [dbo].[Admins]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM [dbo].[Institutes]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM [dbo].[Programs]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM [dbo].[Exams]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM [dbo].[ProgramOfferings]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM [dbo].[ProgramOfferingExamRequirements]", script, StringComparison.OrdinalIgnoreCase);
    }

    [LocalDbFact]
    public async Task Reset_script_removes_student_data_and_preserves_catalog_rows()
    {
        await using var database = new LocalDbTestDatabase($"GraduateAppReset_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        await database.ExecuteAsync(CreateSchemaSql);
        await database.ExecuteSqlServerScriptAsync(ReadScript());

        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Students];"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Applications];"));
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[StudentExamScores];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Admins];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Institutes];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Programs];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Exams];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[ProgramOfferings];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[ProgramOfferingExamRequirements];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[PasswordResetTokens];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[SecurityAuditLogs];"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [dbo].[SecurityAuditLogs] WHERE [TargetType] = N'Admin';"));
    }

    [LocalDbFact]
    public async Task Reset_script_rejects_an_unapproved_database_name_without_deleting_data()
    {
        await using var database = new LocalDbTestDatabase($"GraduateAppUnsafe_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        await database.ExecuteAsync(CreateSchemaSql);

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(
            () => database.ExecuteSqlServerScriptAsync(ReadScript()));

        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Students];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Admins];"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Exams];"));
    }

    private static string ReadScript()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "Reset-StudentTestData.sql"));
    }

    private const string CreateSchemaSql = """
        CREATE TABLE [dbo].[Admins] ([Id] int NOT NULL);
        CREATE TABLE [dbo].[Institutes] ([Id] int NOT NULL);
        CREATE TABLE [dbo].[Programs] ([Id] int NOT NULL);
        CREATE TABLE [dbo].[Exams] ([Id] int NOT NULL);
        CREATE TABLE [dbo].[ProgramOfferings] ([Id] int NOT NULL);
        CREATE TABLE [dbo].[ProgramOfferingExamRequirements] ([Id] int NOT NULL);
        CREATE TABLE [dbo].[Students] ([TC] varchar(11) NOT NULL);
        CREATE TABLE [dbo].[Applications] ([Id] int NOT NULL);
        CREATE TABLE [dbo].[ApplicationScoreSnapshots] ([Id] int NOT NULL);
        CREATE TABLE [dbo].[ReferenceLetters] ([Id] int NOT NULL);
        CREATE TABLE [dbo].[ApplicationStatusHistory] ([Id] int NOT NULL);
        CREATE TABLE [dbo].[StudentExamScores] ([Id] int NOT NULL);
        CREATE TABLE [dbo].[EducationInfo] ([Id] int NOT NULL);
        CREATE TABLE [dbo].[PasswordResetTokens] ([Id] int NOT NULL, [TC] varchar(11) NULL);
        CREATE TABLE [dbo].[SystemLogs] ([Id] int NOT NULL, [TC] varchar(11) NULL);
        CREATE TABLE [dbo].[SecurityAuditLogs] ([Id] int NOT NULL, [TargetType] nvarchar(100) NOT NULL);

        INSERT INTO [dbo].[Admins] VALUES (1);
        INSERT INTO [dbo].[Institutes] VALUES (1);
        INSERT INTO [dbo].[Programs] VALUES (1);
        INSERT INTO [dbo].[Exams] VALUES (1);
        INSERT INTO [dbo].[ProgramOfferings] VALUES (1);
        INSERT INTO [dbo].[ProgramOfferingExamRequirements] VALUES (1);
        INSERT INTO [dbo].[Students] VALUES ('10000000146');
        INSERT INTO [dbo].[Applications] VALUES (1);
        INSERT INTO [dbo].[ApplicationScoreSnapshots] VALUES (1);
        INSERT INTO [dbo].[ReferenceLetters] VALUES (1);
        INSERT INTO [dbo].[ApplicationStatusHistory] VALUES (1);
        INSERT INTO [dbo].[StudentExamScores] VALUES (1);
        INSERT INTO [dbo].[EducationInfo] VALUES (1);
        INSERT INTO [dbo].[PasswordResetTokens] VALUES (1, '10000000146'), (2, NULL);
        INSERT INTO [dbo].[SystemLogs] VALUES (1, '10000000146'), (2, NULL);
        INSERT INTO [dbo].[SecurityAuditLogs] VALUES (1, N'Student'), (2, N'Application'), (3, N'Admin');
        """;
}
