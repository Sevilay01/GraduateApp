using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GraduateApp.API.Migrations
{
    /// <inheritdoc />
    public partial class FixNullableStudentTelephoneUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                SET NOCOUNT ON;

                DECLARE @studentsObjectId int =
                (
                    SELECT t.[object_id]
                    FROM sys.tables AS t
                    INNER JOIN sys.schemas AS s ON s.[schema_id] = t.[schema_id]
                    WHERE s.[name] = N'dbo'
                      AND t.[name] = N'Students'
                );
                IF @studentsObjectId IS NULL
                BEGIN
                    THROW 51120, N'Güvenlik kontrolü: dbo.Students tablosu bulunamadı; migration durduruldu.', 1;
                END;

                DECLARE @telephoneColumnId int =
                (
                    SELECT [column_id]
                    FROM sys.columns
                    WHERE [object_id] = @studentsObjectId
                      AND [name] = N'Telephone'
                );

                IF @telephoneColumnId IS NULL
                BEGIN
                    THROW 51121, N'Güvenlik kontrolü: dbo.Students.Telephone kolonu bulunamadı; migration durduruldu.', 1;
                END;

                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[Students]
                    WHERE [Telephone] IS NOT NULL
                    GROUP BY [Telephone]
                    HAVING COUNT_BIG(*) > 1
                )
                BEGIN
                    THROW 51122, N'Güvenlik kontrolü: dbo.Students.Telephone alanında yinelenen NULL olmayan değerler bulundu. Veri değiştirilmedi.', 1;
                END;

                CREATE TABLE #TelephoneKeyConstraints
                (
                    [ConstraintName] sysname NOT NULL,
                    [ConstraintType] char(2) NOT NULL,
                    [IndexType] tinyint NOT NULL,
                    [IsPrimaryKey] bit NOT NULL,
                    [IsUniqueConstraint] bit NOT NULL,
                    [KeyColumnCount] int NOT NULL,
                    [TelephoneKeyCount] int NOT NULL
                );

                INSERT INTO #TelephoneKeyConstraints
                (
                    [ConstraintName],
                    [ConstraintType],
                    [IndexType],
                    [IsPrimaryKey],
                    [IsUniqueConstraint],
                    [KeyColumnCount],
                    [TelephoneKeyCount]
                )
                SELECT
                    kc.[name],
                    kc.[type],
                    i.[type],
                    i.[is_primary_key],
                    i.[is_unique_constraint],
                    SUM(CASE WHEN ic.[key_ordinal] > 0 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN ic.[key_ordinal] > 0 AND ic.[column_id] = @telephoneColumnId THEN 1 ELSE 0 END)
                FROM sys.key_constraints AS kc
                INNER JOIN sys.indexes AS i
                    ON i.[object_id] = kc.[parent_object_id]
                   AND i.[index_id] = kc.[unique_index_id]
                INNER JOIN sys.index_columns AS ic
                    ON ic.[object_id] = i.[object_id]
                   AND ic.[index_id] = i.[index_id]
                WHERE kc.[parent_object_id] = @studentsObjectId
                GROUP BY
                    kc.[name],
                    kc.[type],
                    i.[type],
                    i.[is_primary_key],
                    i.[is_unique_constraint]
                HAVING SUM(CASE WHEN ic.[key_ordinal] > 0 AND ic.[column_id] = @telephoneColumnId THEN 1 ELSE 0 END) > 0;

                DECLARE @unsupportedConstraintName sysname =
                (
                    SELECT TOP (1) [ConstraintName]
                    FROM #TelephoneKeyConstraints
                    WHERE [ConstraintType] <> 'UQ'
                       OR [IsPrimaryKey] = 1
                       OR [IsUniqueConstraint] = 0
                       OR [IndexType] <> 2
                       OR [KeyColumnCount] <> 1
                       OR [TelephoneKeyCount] <> 1
                    ORDER BY [ConstraintName]
                );

                IF @unsupportedConstraintName IS NOT NULL
                BEGIN
                    DECLARE @unsupportedConstraintMessage nvarchar(2048) =
                        N'Güvenlik kontrolü: dbo.Students.Telephone için desteklenmeyen key constraint bulundu ('
                        + @unsupportedConstraintName
                        + N'). Constraint değiştirilmedi.';
                    THROW 51123, @unsupportedConstraintMessage, 1;
                END;

                IF (SELECT COUNT_BIG(*) FROM #TelephoneKeyConstraints) > 1
                BEGIN
                    THROW 51124, N'Güvenlik kontrolü: dbo.Students.Telephone için birden fazla key constraint bulundu. Hiçbiri değiştirilmedi.', 1;
                END;

                CREATE TABLE #TelephoneNormalUniqueIndexes
                (
                    [IndexId] int NOT NULL PRIMARY KEY,
                    [IndexName] sysname NOT NULL,
                    [IndexType] tinyint NOT NULL,
                    [IsDisabled] bit NOT NULL,
                    [HasFilter] bit NOT NULL,
                    [NormalizedFilter] nvarchar(4000) COLLATE Latin1_General_100_CI_AS NULL,
                    [KeyColumnCount] int NOT NULL,
                    [TelephoneKeyCount] int NOT NULL,
                    [IncludedColumnCount] int NOT NULL
                );

                INSERT INTO #TelephoneNormalUniqueIndexes
                (
                    [IndexId],
                    [IndexName],
                    [IndexType],
                    [IsDisabled],
                    [HasFilter],
                    [NormalizedFilter],
                    [KeyColumnCount],
                    [TelephoneKeyCount],
                    [IncludedColumnCount]
                )
                SELECT
                    i.[index_id],
                    i.[name],
                    i.[type],
                    i.[is_disabled],
                    i.[has_filter],
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        i.[filter_definition],
                        N' ', N''),
                        N'(', N''),
                        N')', N''),
                        N'[', N''),
                        N']', N''),
                        NCHAR(9), N''),
                        NCHAR(10), N''),
                        NCHAR(13), N'') COLLATE Latin1_General_100_CI_AS,
                    SUM(CASE WHEN ic.[key_ordinal] > 0 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN ic.[key_ordinal] > 0 AND ic.[column_id] = @telephoneColumnId THEN 1 ELSE 0 END),
                    SUM(CASE WHEN ic.[is_included_column] = 1 THEN 1 ELSE 0 END)
                FROM sys.indexes AS i
                INNER JOIN sys.index_columns AS ic
                    ON ic.[object_id] = i.[object_id]
                   AND ic.[index_id] = i.[index_id]
                WHERE i.[object_id] = @studentsObjectId
                  AND i.[index_id] > 0
                  AND i.[is_unique] = 1
                  AND i.[is_primary_key] = 0
                  AND i.[is_unique_constraint] = 0
                  AND i.[is_hypothetical] = 0
                GROUP BY
                    i.[index_id],
                    i.[name],
                    i.[type],
                    i.[is_disabled],
                    i.[has_filter],
                    i.[filter_definition]
                HAVING SUM(CASE WHEN ic.[key_ordinal] > 0 AND ic.[column_id] = @telephoneColumnId THEN 1 ELSE 0 END) > 0;

                DECLARE @canonicalIndexName sysname = N'IX_Students_Telephone';
                DECLARE @namedCanonicalIndexId int =
                (
                    SELECT [index_id]
                    FROM sys.indexes
                    WHERE [object_id] = @studentsObjectId
                      AND [name] = @canonicalIndexName
                );
                DECLARE @canonicalIndexId int =
                (
                    SELECT [index_id]
                    FROM sys.indexes
                    WHERE [object_id] = @studentsObjectId
                      AND [name] = @canonicalIndexName
                      AND [is_primary_key] = 0
                      AND [is_unique_constraint] = 0
                );

                IF @namedCanonicalIndexId IS NOT NULL
                   AND @canonicalIndexId IS NULL
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM #TelephoneKeyConstraints
                       WHERE [ConstraintName] = @canonicalIndexName
                   )
                BEGIN
                    THROW 51125, N'Güvenlik kontrolü: IX_Students_Telephone adı beklenmeyen bir index veya constraint tarafından kullanılıyor.', 1;
                END;

                IF @canonicalIndexId IS NOT NULL
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM #TelephoneNormalUniqueIndexes
                       WHERE [IndexId] = @canonicalIndexId
                         AND [IndexType] = 2
                         AND [IsDisabled] = 0
                         AND [HasFilter] = 1
                         AND [NormalizedFilter] = N'TelephoneISNOTNULL' COLLATE Latin1_General_100_CI_AS
                         AND [KeyColumnCount] = 1
                         AND [TelephoneKeyCount] = 1
                         AND [IncludedColumnCount] = 0
                   )
                BEGIN
                    THROW 51125, N'Güvenlik kontrolü: IX_Students_Telephone mevcut ancak beklenen filtered unique index imzasıyla eşleşmiyor.', 1;
                END;

                IF EXISTS
                (
                    SELECT 1
                    FROM #TelephoneNormalUniqueIndexes
                    WHERE [IndexName] <> @canonicalIndexName
                      AND
                      (
                          [IndexType] <> 2
                          OR [IsDisabled] <> 0
                          OR [KeyColumnCount] <> 1
                          OR [TelephoneKeyCount] <> 1
                          OR [IncludedColumnCount] <> 0
                          OR
                          (
                              [HasFilter] = 1
                              AND ISNULL([NormalizedFilter], N'') COLLATE Latin1_General_100_CI_AS
                                  <> N'TelephoneISNOTNULL' COLLATE Latin1_General_100_CI_AS
                          )
                      )
                )
                BEGIN
                    THROW 51126, N'Güvenlik kontrolü: dbo.Students.Telephone üzerinde güvenle dönüştürülemeyen unique index bulundu. Index değiştirilmedi.', 1;
                END;

                IF
                (
                    SELECT COUNT_BIG(*)
                    FROM #TelephoneNormalUniqueIndexes
                    WHERE [IndexName] <> @canonicalIndexName
                      AND [HasFilter] = 1
                      AND [NormalizedFilter] = N'TelephoneISNOTNULL' COLLATE Latin1_General_100_CI_AS
                ) > 1
                OR
                (
                    SELECT COUNT_BIG(*)
                    FROM #TelephoneNormalUniqueIndexes
                    WHERE [IndexName] <> @canonicalIndexName
                      AND [HasFilter] = 0
                ) > 1
                BEGIN
                    THROW 51127, N'Güvenlik kontrolü: dbo.Students.Telephone üzerinde birden fazla eşdeğer unique index bulundu. Indexler değiştirilmedi.', 1;
                END;

                IF @canonicalIndexId IS NOT NULL
                   AND EXISTS
                   (
                       SELECT 1
                       FROM #TelephoneNormalUniqueIndexes
                       WHERE [IndexName] <> @canonicalIndexName
                         AND [HasFilter] = 1
                         AND [NormalizedFilter] = N'TelephoneISNOTNULL' COLLATE Latin1_General_100_CI_AS
                   )
                BEGIN
                    THROW 51128, N'Güvenlik kontrolü: canonical ve eşdeğer isimli iki filtered Telephone indexi bulundu. Indexler değiştirilmedi.', 1;
                END;

                DECLARE @legacyConstraintName sysname =
                (
                    SELECT [ConstraintName]
                    FROM #TelephoneKeyConstraints
                );
                DECLARE @sql nvarchar(max);

                IF @legacyConstraintName IS NOT NULL
                BEGIN
                    SET @sql = N'ALTER TABLE ' + QUOTENAME(N'dbo') + N'.' + QUOTENAME(N'Students')
                        + N' DROP CONSTRAINT ' + QUOTENAME(@legacyConstraintName) + N';';
                    EXEC sys.sp_executesql @sql;
                    IF @legacyConstraintName = @canonicalIndexName
                    BEGIN
                        SET @canonicalIndexId = NULL;
                    END;
                END;

                DECLARE @unfilteredIndexName sysname =
                (
                    SELECT [IndexName]
                    FROM #TelephoneNormalUniqueIndexes
                    WHERE [IndexName] <> @canonicalIndexName
                      AND [HasFilter] = 0
                );

                IF @unfilteredIndexName IS NOT NULL
                BEGIN
                    SET @sql = N'DROP INDEX ' + QUOTENAME(@unfilteredIndexName)
                        + N' ON ' + QUOTENAME(N'dbo') + N'.' + QUOTENAME(N'Students') + N';';
                    EXEC sys.sp_executesql @sql;
                END;

                DECLARE @equivalentFilteredIndexName sysname =
                (
                    SELECT [IndexName]
                    FROM #TelephoneNormalUniqueIndexes
                    WHERE [IndexName] <> @canonicalIndexName
                      AND [HasFilter] = 1
                      AND [NormalizedFilter] = N'TelephoneISNOTNULL' COLLATE Latin1_General_100_CI_AS
                );

                IF @canonicalIndexId IS NULL AND @equivalentFilteredIndexName IS NOT NULL
                BEGIN
                    DECLARE @qualifiedIndexName nvarchar(776) =
                        QUOTENAME(N'dbo') + N'.' + QUOTENAME(N'Students') + N'.' + QUOTENAME(@equivalentFilteredIndexName);
                    EXEC sys.sp_rename
                        @objname = @qualifiedIndexName,
                        @newname = @canonicalIndexName,
                        @objtype = N'INDEX';
                    SET @canonicalIndexId =
                    (
                        SELECT [index_id]
                        FROM sys.indexes
                        WHERE [object_id] = @studentsObjectId
                          AND [name] = @canonicalIndexName
                    );
                END;

                IF @canonicalIndexId IS NULL
                BEGIN
                    EXEC sys.sp_executesql
                        N'CREATE UNIQUE NONCLUSTERED INDEX [IX_Students_Telephone]
                          ON [dbo].[Students] ([Telephone])
                          WHERE [Telephone] IS NOT NULL;';
                END;

                DECLARE @verificationIndexFound bit = 0;
                DECLARE @verificationIndexType tinyint = NULL;
                DECLARE @verificationIsUnique bit = NULL;
                DECLARE @verificationIsPrimaryKey bit = NULL;
                DECLARE @verificationIsUniqueConstraint bit = NULL;
                DECLARE @verificationIsDisabled bit = NULL;
                DECLARE @verificationHasFilter bit = NULL;
                DECLARE @verificationFilterMatches bit = 0;
                DECLARE @verificationKeyColumnCount bigint = NULL;
                DECLARE @verificationTelephoneOrdinalOneCount bigint = NULL;
                DECLARE @verificationIncludedColumnCount bigint = NULL;

                SELECT
                    @verificationIndexFound = 1,
                    @verificationIndexType = i.[type],
                    @verificationIsUnique = i.[is_unique],
                    @verificationIsPrimaryKey = i.[is_primary_key],
                    @verificationIsUniqueConstraint = i.[is_unique_constraint],
                    @verificationIsDisabled = i.[is_disabled],
                    @verificationHasFilter = i.[has_filter],
                    @verificationFilterMatches =
                        CASE
                            WHEN REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                                i.[filter_definition],
                                N' ', N''),
                                N'(', N''),
                                N')', N''),
                                N'[', N''),
                                N']', N''),
                                NCHAR(9), N''),
                                NCHAR(10), N''),
                                NCHAR(13), N'') COLLATE Latin1_General_100_CI_AS
                                = N'TelephoneISNOTNULL' COLLATE Latin1_General_100_CI_AS
                            THEN 1
                            ELSE 0
                        END,
                    @verificationKeyColumnCount =
                    (
                        SELECT COUNT_BIG(*)
                        FROM sys.index_columns AS ic
                        WHERE ic.[object_id] = i.[object_id]
                          AND ic.[index_id] = i.[index_id]
                          AND ic.[key_ordinal] > 0
                    ),
                    @verificationTelephoneOrdinalOneCount =
                    (
                        SELECT COUNT_BIG(*)
                        FROM sys.index_columns AS ic
                        WHERE ic.[object_id] = i.[object_id]
                          AND ic.[index_id] = i.[index_id]
                          AND ic.[key_ordinal] = 1
                          AND ic.[column_id] = @telephoneColumnId
                          AND ic.[is_included_column] = 0
                    ),
                    @verificationIncludedColumnCount =
                    (
                        SELECT COUNT_BIG(*)
                        FROM sys.index_columns AS ic
                        WHERE ic.[object_id] = i.[object_id]
                          AND ic.[index_id] = i.[index_id]
                          AND ic.[is_included_column] = 1
                    )
                FROM sys.indexes AS i
                WHERE i.[object_id] = @studentsObjectId
                  AND i.[name] = @canonicalIndexName;

                IF @verificationIndexFound <> 1
                   OR ISNULL(@verificationIndexType, 0) <> 2
                   OR ISNULL(@verificationIsUnique, 0) <> 1
                   OR ISNULL(@verificationIsPrimaryKey, 1) <> 0
                   OR ISNULL(@verificationIsUniqueConstraint, 1) <> 0
                   OR ISNULL(@verificationIsDisabled, 1) <> 0
                   OR ISNULL(@verificationHasFilter, 0) <> 1
                   OR ISNULL(@verificationFilterMatches, 0) <> 1
                   OR ISNULL(@verificationKeyColumnCount, 0) <> 1
                   OR ISNULL(@verificationTelephoneOrdinalOneCount, 0) <> 1
                   OR ISNULL(@verificationIncludedColumnCount, 0) <> 0
                BEGIN
                    DECLARE @verificationMessage nvarchar(2048) = CONCAT(
                        N'Güvenlik kontrolü: canonical filtered Telephone indexi doğrulanamadı. ',
                        N'index_found=', @verificationIndexFound,
                        N'; type=', COALESCE(CONVERT(nvarchar(10), @verificationIndexType), N'NULL'),
                        N'; is_unique=', COALESCE(CONVERT(nvarchar(1), @verificationIsUnique), N'NULL'),
                        N'; is_primary_key=', COALESCE(CONVERT(nvarchar(1), @verificationIsPrimaryKey), N'NULL'),
                        N'; is_unique_constraint=', COALESCE(CONVERT(nvarchar(1), @verificationIsUniqueConstraint), N'NULL'),
                        N'; is_disabled=', COALESCE(CONVERT(nvarchar(1), @verificationIsDisabled), N'NULL'),
                        N'; has_filter=', COALESCE(CONVERT(nvarchar(1), @verificationHasFilter), N'NULL'),
                        N'; filter_match=', @verificationFilterMatches,
                        N'; key_column_count=', COALESCE(CONVERT(nvarchar(20), @verificationKeyColumnCount), N'NULL'),
                        N'; telephone_ordinal_1_count=', COALESCE(CONVERT(nvarchar(20), @verificationTelephoneOrdinalOneCount), N'NULL'),
                        N'; include_count=', COALESCE(CONVERT(nvarchar(20), @verificationIncludedColumnCount), N'NULL'),
                        N'.');
                    THROW 51129, @verificationMessage, 1;
                END;

                DROP TABLE #TelephoneNormalUniqueIndexes;
                DROP TABLE #TelephoneKeyConstraints;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Bu migration güvenli biçimde geri alınamaz. Filtrelenmemiş UNIQUE constraint, birden fazla NULL Telephone kaydını reddedebilir; veri silinmeyecektir.");
        }
    }
}
