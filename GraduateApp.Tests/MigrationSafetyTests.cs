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

        var targetInspection = Find(script, "UPDATE target");
        var dependencyDrop = Find(script, "DECLARE harden_default_drop_cursor");
        var alterExecution = Find(script, "DECLARE harden_alter_column_cursor");

        Assert.True(targetInspection < dependencyDrop, "Expected target schema inspection before dependency teardown.");
        Assert.True(dependencyDrop < alterExecution, "Expected dependency teardown before conditional ALTER execution.");

        AssertCanonicalDefaultOrder(script, alterExecution, "DF_Applications_ApplicationDate");
        AssertCanonicalDefaultOrder(script, alterExecution, "DF_Applications_CurrentStatus");
        AssertCanonicalDefaultOrder(script, alterExecution, "DF_ApplicationStatusHistory_ChangeDate");
        AssertCanonicalDefaultOrder(script, alterExecution, "DF_PasswordResetTokens_IsUsed");
    }

    [Fact]
    public void HardenExistingSchema_captures_and_restores_supported_index_metadata_around_conditional_alters()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppMigrationScriptTest;Integrated Security=true")
            .Options;

        using var dbContext = new GraduateAppDbContext(options);
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        AssertOrdered(script, "CREATE TABLE #HardenIndexes", "DECLARE harden_index_drop_cursor");
        AssertOrdered(script, "DECLARE harden_index_drop_cursor", "DECLARE harden_alter_column_cursor");
        AssertOrdered(script, "DECLARE harden_alter_column_cursor", "DECLARE harden_index_restore_cursor");
        AssertOrdered(script, "DECLARE harden_index_restore_cursor", "DROP TABLE IF EXISTS #HardenIndexes");

        Assert.Contains("[IndexTypeDescription]", script, StringComparison.Ordinal);
        Assert.Contains("[IsUniqueConstraint]", script, StringComparison.Ordinal);
        Assert.Contains("[IsDescending]", script, StringComparison.Ordinal);
        Assert.Contains("[IsIncluded]", script, StringComparison.Ordinal);
        Assert.Contains("[FilterDefinition]", script, StringComparison.Ordinal);
        Assert.Contains("[IsDisabled]", script, StringComparison.Ordinal);
        Assert.Contains("[FillFactor]", script, StringComparison.Ordinal);
        Assert.Contains("[DataSpaceName]", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51007, @unsafePrimaryKeyMessage", script, StringComparison.Ordinal);
        Assert.Contains("QUOTENAME(@dropIndexName)", script, StringComparison.Ordinal);
        Assert.Contains("STRING_AGG", script, StringComparison.Ordinal);
    }

    [Fact]
    public void HardenExistingSchema_preserves_supported_unique_constraints_as_key_constraints()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppMigrationScriptTest;Integrated Security=true")
            .Options;

        using var dbContext = new GraduateAppDbContext(options);
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        AssertOrdered(script, "DECLARE harden_index_drop_cursor", "DECLARE harden_unique_constraint_drop_cursor");
        AssertOrdered(script, "DECLARE harden_unique_constraint_drop_cursor", "DECLARE harden_default_drop_cursor");
        AssertOrdered(script, "DECLARE harden_default_drop_cursor", "DECLARE harden_alter_column_cursor");
        AssertOrdered(script, "DECLARE harden_alter_column_cursor", "DECLARE harden_unique_constraint_restore_cursor");
        AssertOrdered(script, "DECLARE harden_unique_constraint_restore_cursor", "DECLARE harden_index_restore_cursor");
        AssertOrdered(script, "DECLARE harden_index_restore_cursor", "DECLARE harden_foreign_key_restore_cursor");

        Assert.Contains("CREATE TABLE #HardenKeyConstraints", script, StringComparison.Ordinal);
        Assert.Contains("[ConstraintObjectId]", script, StringComparison.Ordinal);
        Assert.Contains("[ConstraintType]", script, StringComparison.Ordinal);
        Assert.Contains("[IsAlterTargetRelated]", script, StringComparison.Ordinal);
        Assert.Contains("[InboundForeignKeyCount]", script, StringComparison.Ordinal);
        Assert.Contains("WITHIN GROUP (ORDER BY [KeyOrdinal])", script, StringComparison.Ordinal);
        Assert.Contains("N' ADD CONSTRAINT ' + QUOTENAME(@restoreUniqueConstraintName)", script, StringComparison.Ordinal);
        Assert.Contains("N' UNIQUE ' + CASE WHEN @restoreUniqueConstraintIndexType = 1", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51007, @unsafePrimaryKeyMessage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE UNIQUE INDEX ' + QUOTENAME(@restoreUniqueConstraintName)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void HardenExistingSchema_uses_narrow_surrogate_keys_for_temporary_metadata_tables()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppMigrationScriptTest;Integrated Security=true")
            .Options;

        using var dbContext = new GraduateAppDbContext(options);
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("[Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIMARY KEY ([SchemaName]", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIMARY KEY ([TableName]", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIMARY KEY ([ConstraintName]", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIMARY KEY ([SchemaName], [TableName]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void HardenExistingSchema_validates_migration_owned_index_signatures()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppMigrationScriptTest;Integrated Security=true")
            .Options;

        using var dbContext = new GraduateAppDbContext(options);
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("IX_Admins_NormalizedEmail", script, StringComparison.Ordinal);
        Assert.Contains("IX_Students_NormalizedEmail", script, StringComparison.Ordinal);
        Assert.Contains("IX_Applications_TC_ProgramID", script, StringComparison.Ordinal);
        Assert.Contains("IX_PasswordResetTokens_AdminID", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51010, @invalidExpectedIndexMessage", script, StringComparison.Ordinal);
        Assert.Contains("EXCEPT", script, StringComparison.Ordinal);
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

    [Fact]
    public void FixNullableStudentTelephoneUniqueness_is_metadata_driven_and_does_not_delete_data()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppMigrationScriptTest;Integrated Security=true")
            .Options;

        using var dbContext = new GraduateAppDbContext(options);
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: "20260717065942_HardenExistingSchema",
            toMigration: "20260717110647_FixNullableStudentTelephoneUniqueness");

        Assert.Contains("sys.key_constraints", script, StringComparison.Ordinal);
        Assert.Contains("sys.indexes", script, StringComparison.Ordinal);
        Assert.Contains("sys.index_columns", script, StringComparison.Ordinal);
        Assert.Contains("sys.columns", script, StringComparison.Ordinal);
        Assert.Contains("sys.tables", script, StringComparison.Ordinal);
        Assert.Contains("sys.schemas", script, StringComparison.Ordinal);
        Assert.Contains("QUOTENAME(@legacyConstraintName)", script, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT", script, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE NONCLUSTERED INDEX [IX_Students_Telephone]", script, StringComparison.Ordinal);
        Assert.Contains("WHERE [Telephone] IS NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51122", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51123", script, StringComparison.Ordinal);
        Assert.Contains("COLLATE Latin1_General_100_CI_AS", script, StringComparison.Ordinal);
        Assert.DoesNotContain("LOWER(", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@verificationIndexFound", script, StringComparison.Ordinal);
        Assert.Contains("@verificationIndexType", script, StringComparison.Ordinal);
        Assert.Contains("@verificationIsUnique", script, StringComparison.Ordinal);
        Assert.Contains("@verificationIsPrimaryKey", script, StringComparison.Ordinal);
        Assert.Contains("@verificationIsUniqueConstraint", script, StringComparison.Ordinal);
        Assert.Contains("@verificationIsDisabled", script, StringComparison.Ordinal);
        Assert.Contains("@verificationHasFilter", script, StringComparison.Ordinal);
        Assert.Contains("@verificationFilterMatches", script, StringComparison.Ordinal);
        Assert.Contains("@verificationKeyColumnCount", script, StringComparison.Ordinal);
        Assert.Contains("@verificationTelephoneOrdinalOneCount", script, StringComparison.Ordinal);
        Assert.Contains("@verificationIncludedColumnCount", script, StringComparison.Ordinal);
        Assert.Contains("filter_match=", script, StringComparison.Ordinal);
        Assert.DoesNotContain("UQ__Students__D9FEB744290C1415", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE TABLE", script, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertCanonicalDefaultOrder(
        string script,
        int alterExecution,
        string canonicalDefaultToken)
    {
        var canonicalDefault = Find(script, canonicalDefaultToken);

        Assert.True(
            alterExecution < canonicalDefault,
            $"Expected '{canonicalDefaultToken}' after conditional ALTER execution.");
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
