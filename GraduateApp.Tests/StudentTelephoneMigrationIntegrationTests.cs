using Microsoft.Data.SqlClient;

namespace GraduateApp.Tests;

public sealed class StudentTelephoneMigrationIntegrationTests
{
    private const string BasicStudentsTable =
        """
        CREATE TABLE [dbo].[Students]
        (
            [TC] char(11) NOT NULL,
            [Email] varchar(254) NULL,
            [Telephone] varchar(15) NULL,
            CONSTRAINT [PK_Students_Test] PRIMARY KEY ([TC])
        );
        """;

    [LocalDbFact]
    public async Task Migration_replaces_randomly_named_legacy_constraint_and_enforces_filtered_uniqueness()
    {
        await using var database = await LocalDbTestSupport.CreateDatabaseAsync(
            BasicStudentsTable
            + "ALTER TABLE [dbo].[Students] ADD UNIQUE NONCLUSTERED ([Telephone]);");

        var legacyConstraintName = await database.ScalarAsync<string>(
            """
            SELECT kc.[name]
            FROM sys.key_constraints AS kc
            WHERE kc.[parent_object_id] = OBJECT_ID(N'[dbo].[Students]')
              AND kc.[type] = 'UQ';
            """);
        Assert.StartsWith("UQ__Students__", legacyConstraintName, StringComparison.Ordinal);

        await database.MigrateAsync();

        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.key_constraints
                WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[Students]')
                  AND [type] = 'UQ';
                """));
        await AssertCanonicalIndexAsync(database);

        await database.ExecuteAsync(
            """
            INSERT INTO [dbo].[Students] ([TC], [Email], [Telephone]) VALUES
                ('10000000001', 'one@example.test', NULL),
                ('10000000002', 'two@example.test', NULL),
                ('10000000003', 'three@example.test', '555000000000001'),
                ('10000000004', 'four@example.test', '555000000000002');
            """);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
            """
            INSERT INTO [dbo].[Students] ([TC], [Email], [Telephone])
            VALUES ('10000000005', 'five@example.test', '555000000000001');
            """));
        Assert.Contains(duplicate.Number, new[] { 2601, 2627 });
        Assert.Equal(4, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Students];"));
    }

    [LocalDbFact]
    public async Task Migration_stops_without_modifying_composite_unique_constraint()
    {
        await using var database = await LocalDbTestSupport.CreateDatabaseAsync(
            BasicStudentsTable
            + "ALTER TABLE [dbo].[Students] ADD CONSTRAINT [UQ_Students_Telephone_Email_Test] UNIQUE ([Telephone], [Email]);");

        var exception = await Assert.ThrowsAsync<SqlException>(() => database.MigrateAsync());

        Assert.Equal(51123, exception.Number);
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.key_constraints
                WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[Students]')
                  AND [name] = N'UQ_Students_Telephone_Email_Test';
                """));
        Assert.Equal(0, await CurrentMigrationHistoryCountAsync(database));
    }

    [LocalDbFact]
    public async Task Migration_stops_without_modifying_primary_key_on_telephone()
    {
        await using var database = await LocalDbTestSupport.CreateDatabaseAsync(
            """
            CREATE TABLE [dbo].[Students]
            (
                [TC] char(11) NULL,
                [Telephone] varchar(15) NOT NULL,
                CONSTRAINT [PK_Students_Telephone_Test] PRIMARY KEY ([Telephone])
            );
            """);

        var exception = await Assert.ThrowsAsync<SqlException>(() => database.MigrateAsync());

        Assert.Equal(51123, exception.Number);
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.key_constraints
                WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[Students]')
                  AND [name] = N'PK_Students_Telephone_Test'
                  AND [type] = 'PK';
                """));
        Assert.Equal(0, await CurrentMigrationHistoryCountAsync(database));
    }

    [LocalDbFact]
    public async Task Migration_stops_before_schema_changes_when_non_null_duplicates_exist()
    {
        await using var database = await LocalDbTestSupport.CreateDatabaseAsync(
            BasicStudentsTable
            + """
            INSERT INTO [dbo].[Students] ([TC], [Telephone]) VALUES
                ('10000000001', '555000000000001'),
                ('10000000002', '555000000000001');
            """);

        var exception = await Assert.ThrowsAsync<SqlException>(() => database.MigrateAsync());

        Assert.Equal(51122, exception.Number);
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Students];"));
        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'[dbo].[Students]')
                  AND [name] = N'IX_Students_Telephone';
                """));
        Assert.Equal(0, await CurrentMigrationHistoryCountAsync(database));
    }

    [LocalDbFact]
    public async Task Migration_keeps_an_existing_canonical_index_unchanged_on_subsequent_update()
    {
        await using var database = await LocalDbTestSupport.CreateDatabaseAsync(
            BasicStudentsTable
            + """
            CREATE UNIQUE NONCLUSTERED INDEX [IX_Students_Telephone]
            ON [dbo].[Students] ([Telephone])
            WHERE ([Telephone] IS NOT NULL);
            """);
        var originalIndexId = await CanonicalIndexIdAsync(database);

        await database.MigrateAsync();
        await database.MigrateAsync();

        Assert.Equal(originalIndexId, await CanonicalIndexIdAsync(database));
        Assert.Equal(1, await CurrentMigrationHistoryCountAsync(database));
        await AssertCanonicalIndexAsync(database);
    }

    [LocalDbFact]
    public async Task Migration_safely_renames_an_equivalent_filtered_unique_index()
    {
        await using var database = await LocalDbTestSupport.CreateDatabaseAsync(
            BasicStudentsTable
            + """
            CREATE UNIQUE NONCLUSTERED INDEX [UX_Students_Telephone_Legacy]
            ON [dbo].[Students] ([Telephone])
            WHERE [Telephone] IS NOT NULL;
            """);
        var originalIndexId = await database.ScalarAsync<int>(
            """
            SELECT [index_id]
            FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[Students]')
              AND [name] = N'UX_Students_Telephone_Legacy';
            """);

        await database.MigrateAsync();

        Assert.Equal(originalIndexId, await CanonicalIndexIdAsync(database));
        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'[dbo].[Students]')
                  AND [name] = N'UX_Students_Telephone_Legacy';
                """));
        await AssertCanonicalIndexAsync(database);
    }

    [LocalDbFact]
    public async Task Migration_replaces_a_differently_named_unfiltered_normal_unique_index()
    {
        await using var database = await LocalDbTestSupport.CreateDatabaseAsync(
            BasicStudentsTable
            + """
            CREATE UNIQUE NONCLUSTERED INDEX [UX_Students_Telephone_Unfiltered]
            ON [dbo].[Students] ([Telephone]);
            """);

        await database.MigrateAsync();

        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'[dbo].[Students]')
                  AND [name] = N'UX_Students_Telephone_Unfiltered';
                """));
        await AssertCanonicalIndexAsync(database);
        await database.ExecuteAsync(
            """
            INSERT INTO [dbo].[Students] ([TC], [Telephone]) VALUES
                ('10000000001', NULL),
                ('10000000002', NULL);
            """);
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Students];"));
    }

    private static Task<int> CurrentMigrationHistoryCountAsync(LocalDbTestDatabase database) =>
        database.ScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM [dbo].[__EFMigrationsHistory]
            WHERE [MigrationId] = N'20260717110647_FixNullableStudentTelephoneUniqueness';
            """);

    private static Task<int> CanonicalIndexIdAsync(LocalDbTestDatabase database) =>
        database.ScalarAsync<int>(
            """
            SELECT [index_id]
            FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[Students]')
              AND [name] = N'IX_Students_Telephone';
            """);

    private static async Task AssertCanonicalIndexAsync(LocalDbTestDatabase database)
    {
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.indexes AS i
                WHERE i.[object_id] = OBJECT_ID(N'[dbo].[Students]')
                  AND i.[name] = N'IX_Students_Telephone'
                  AND i.[type_desc] = N'NONCLUSTERED'
                  AND i.[is_unique] = 1
                  AND i.[is_primary_key] = 0
                  AND i.[is_unique_constraint] = 0
                  AND i.[is_disabled] = 0
                  AND i.[has_filter] = 1;
                """));

        var filter = await database.ScalarAsync<string>(
            """
            SELECT [filter_definition]
            FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[Students]')
              AND [name] = N'IX_Students_Telephone';
            """);
        Assert.Equal(
            "telephoneisnotnull",
            filter.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("(", string.Empty, StringComparison.Ordinal)
                .Replace(")", string.Empty, StringComparison.Ordinal)
                .Replace("[", string.Empty, StringComparison.Ordinal)
                .Replace("]", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant());

        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.indexes AS i
                INNER JOIN sys.index_columns AS ic
                    ON ic.[object_id] = i.[object_id]
                   AND ic.[index_id] = i.[index_id]
                INNER JOIN sys.columns AS c
                    ON c.[object_id] = ic.[object_id]
                   AND c.[column_id] = ic.[column_id]
                WHERE i.[object_id] = OBJECT_ID(N'[dbo].[Students]')
                  AND i.[name] = N'IX_Students_Telephone'
                  AND ic.[key_ordinal] = 1
                  AND ic.[is_descending_key] = 0
                  AND ic.[is_included_column] = 0
                  AND c.[name] = N'Telephone';
                """));
    }
}
