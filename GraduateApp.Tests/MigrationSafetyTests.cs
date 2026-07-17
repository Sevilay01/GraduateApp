using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GraduateApp.Tests;

public sealed class MigrationSafetyTests
{
    [Fact]
    public void HardenExistingSchema_orders_dependency_teardown_before_alter_and_defaults_after_alter()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppMigrationScriptTest;Integrated Security=true")
            .Options;

        using var dbContext = new GraduateAppDbContext(options);
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        var dependencyDrop = Find(script, "DECLARE harden_default_drop_cursor");

        AssertCanonicalDefaultOrder(
            script,
            dependencyDrop,
            "ALTER TABLE [dbo].[Applications] ALTER COLUMN [ApplicationDate] datetime2 NOT NULL",
            "DF_Applications_ApplicationDate");
        AssertCanonicalDefaultOrder(
            script,
            dependencyDrop,
            "ALTER TABLE [dbo].[Applications] ALTER COLUMN [CurrentStatus] nvarchar(50) NOT NULL",
            "DF_Applications_CurrentStatus");
        AssertCanonicalDefaultOrder(
            script,
            dependencyDrop,
            "ALTER TABLE [dbo].[ApplicationStatusHistory] ALTER COLUMN [ChangeDate] datetime2 NOT NULL",
            "DF_ApplicationStatusHistory_ChangeDate");
        AssertCanonicalDefaultOrder(
            script,
            dependencyDrop,
            "ALTER TABLE [dbo].[PasswordResetTokens] ALTER COLUMN [IsUsed] bit NOT NULL",
            "DF_PasswordResetTokens_IsUsed");
    }

    [Fact]
    public void HardenExistingSchema_defers_new_column_references_to_dynamic_batches()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppMigrationScriptTest;Integrated Security=true")
            .Options;

        using var dbContext = new GraduateAppDbContext(options);
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        AssertOrdered(
            script,
            "ALTER TABLE [dbo].[Admins] ADD [NormalizedEmail]",
            "EXEC sys.sp_executesql N'UPDATE [dbo].[Admins]");
        AssertOrdered(
            script,
            "ALTER TABLE [dbo].[Students] ADD [NormalizedEmail]",
            "EXEC sys.sp_executesql N'UPDATE [dbo].[Students]");
        AssertOrdered(
            script,
            "ALTER TABLE [dbo].[PasswordResetTokens] ADD [AdminID]",
            "EXEC sys.sp_executesql N'ALTER TABLE [dbo].[PasswordResetTokens] ADD CONSTRAINT [FK_PasswordResetTokens_Admins_AdminID]");
        AssertOrdered(
            script,
            "CREATE TABLE [dbo].[SecurityAuditLogs]",
            "EXEC sys.sp_executesql N'CREATE INDEX [IX_SecurityAuditLogs_CreatedAtUtc]");

        Assert.Contains("N'Sisteme Alındı'", script, StringComparison.Ordinal);
        Assert.Contains("N'İnceleniyor'", script, StringComparison.Ordinal);
    }

    private static void AssertCanonicalDefaultOrder(
        string script,
        int dependencyDrop,
        string alterToken,
        string canonicalDefaultToken)
    {
        var alter = Find(script, alterToken);
        var canonicalDefault = Find(script, canonicalDefaultToken);

        Assert.True(
            dependencyDrop < alter && alter < canonicalDefault,
            $"Expected dependency teardown before '{alterToken}' and '{canonicalDefaultToken}' after it.");
    }

    private static void AssertOrdered(string script, string firstToken, string secondToken)
    {
        var first = Find(script, firstToken);
        var second = Find(script, secondToken);

        Assert.True(first < second, $"Expected '{firstToken}' before '{secondToken}'.");
    }

    private static int Find(string script, string token)
    {
        var position = script.IndexOf(token, StringComparison.Ordinal);
        Assert.True(position >= 0, $"Generated migration script did not contain '{token}'.");
        return position;
    }
}
