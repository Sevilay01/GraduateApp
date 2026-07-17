using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GraduateApp.API.Migrations;

public partial class HardenExistingSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Preflight checks reference only the schema that predates this migration.
        migrationBuilder.Sql(
            """
            SET XACT_ABORT ON;

            IF OBJECT_ID(N'[dbo].[Students]', N'U') IS NULL
                OR OBJECT_ID(N'[dbo].[Admins]', N'U') IS NULL
                OR OBJECT_ID(N'[dbo].[Programs]', N'U') IS NULL
                OR OBJECT_ID(N'[dbo].[Applications]', N'U') IS NULL
                OR OBJECT_ID(N'[dbo].[ApplicationStatusHistory]', N'U') IS NULL
                OR OBJECT_ID(N'[dbo].[PasswordResetTokens]', N'U') IS NULL
            BEGIN
                THROW 51000, 'GraduateApp temel şeması bulunamadı. Bu migration mevcut database-first şemasını güvenli biçimde sertleştirmek içindir.', 1;
            END;

            IF EXISTS (
                SELECT 1 FROM [dbo].[Applications]
                GROUP BY [TC], [ProgramID]
                HAVING COUNT_BIG(*) > 1)
            BEGIN
                THROW 51001, 'Aynı öğrenci ve program için yinelenen başvurular var. Veri silinmedi; unique index öncesi kayıtlar manuel incelenmelidir.', 1;
            END;

            IF EXISTS (
                SELECT 1 FROM [dbo].[Applications]
                WHERE [CurrentStatus] IS NOT NULL
                  AND [CurrentStatus] NOT IN (N'Pending',N'UnderReview',N'Approved',N'Rejected',N'Withdrawn',N'Sisteme Alındı',N'Onay Bekliyor',N'İnceleniyor',N'Onaylandı',N'Reddedildi'))
            BEGIN
                THROW 51002, 'Tanımsız başvuru durumu bulundu. Veri değiştirilmedi; durumlar manuel eşleştirilmelidir.', 1;
            END;
            """);

        // Add all columns first. Any SQL that references them runs in a later DbCommand.
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.Admins', N'NormalizedEmail') IS NULL
                ALTER TABLE [dbo].[Admins] ADD [NormalizedEmail] nvarchar(254) NULL;
            IF COL_LENGTH(N'dbo.Admins', N'SecurityStamp') IS NULL
                ALTER TABLE [dbo].[Admins] ADD [SecurityStamp] varchar(64) NOT NULL CONSTRAINT [DF_Admins_SecurityStamp] DEFAULT (REPLACE(CONVERT(varchar(36), NEWID()), '-', ''));
            IF COL_LENGTH(N'dbo.Admins', N'AccessFailedCount') IS NULL
                ALTER TABLE [dbo].[Admins] ADD [AccessFailedCount] int NOT NULL CONSTRAINT [DF_Admins_AccessFailedCount] DEFAULT (0);
            IF COL_LENGTH(N'dbo.Admins', N'LockoutEndUtc') IS NULL
                ALTER TABLE [dbo].[Admins] ADD [LockoutEndUtc] datetimeoffset NULL;
            IF COL_LENGTH(N'dbo.Admins', N'MustChangePassword') IS NULL
                ALTER TABLE [dbo].[Admins] ADD [MustChangePassword] bit NOT NULL CONSTRAINT [DF_Admins_MustChangePassword] DEFAULT (1);
            IF COL_LENGTH(N'dbo.Admins', N'CreatedAtUtc') IS NULL
                ALTER TABLE [dbo].[Admins] ADD [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_Admins_CreatedAtUtc] DEFAULT (SYSUTCDATETIME());
            IF COL_LENGTH(N'dbo.Admins', N'UpdatedAtUtc') IS NULL
                ALTER TABLE [dbo].[Admins] ADD [UpdatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_Admins_UpdatedAtUtc] DEFAULT (SYSUTCDATETIME());

            IF COL_LENGTH(N'dbo.Students', N'NormalizedEmail') IS NULL
                ALTER TABLE [dbo].[Students] ADD [NormalizedEmail] nvarchar(254) NULL;
            IF COL_LENGTH(N'dbo.Students', N'SecurityStamp') IS NULL
                ALTER TABLE [dbo].[Students] ADD [SecurityStamp] varchar(64) NOT NULL CONSTRAINT [DF_Students_SecurityStamp] DEFAULT (REPLACE(CONVERT(varchar(36), NEWID()), '-', ''));
            IF COL_LENGTH(N'dbo.Students', N'AccessFailedCount') IS NULL
                ALTER TABLE [dbo].[Students] ADD [AccessFailedCount] int NOT NULL CONSTRAINT [DF_Students_AccessFailedCount] DEFAULT (0);
            IF COL_LENGTH(N'dbo.Students', N'LockoutEndUtc') IS NULL
                ALTER TABLE [dbo].[Students] ADD [LockoutEndUtc] datetimeoffset NULL;
            IF COL_LENGTH(N'dbo.Students', N'CreatedAtUtc') IS NULL
                ALTER TABLE [dbo].[Students] ADD [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_Students_CreatedAtUtc] DEFAULT (SYSUTCDATETIME());
            IF COL_LENGTH(N'dbo.Students', N'UpdatedAtUtc') IS NULL
                ALTER TABLE [dbo].[Students] ADD [UpdatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_Students_UpdatedAtUtc] DEFAULT (SYSUTCDATETIME());

            IF COL_LENGTH(N'dbo.Programs', N'IsOpen') IS NULL
                ALTER TABLE [dbo].[Programs] ADD [IsOpen] bit NOT NULL CONSTRAINT [DF_Programs_IsOpen] DEFAULT (0);
            IF COL_LENGTH(N'dbo.Programs', N'ApplicationDeadlineUtc') IS NULL
                ALTER TABLE [dbo].[Programs] ADD [ApplicationDeadlineUtc] datetime2 NULL;

            IF COL_LENGTH(N'dbo.Applications', N'RowVersion') IS NULL
                ALTER TABLE [dbo].[Applications] ADD [RowVersion] rowversion NOT NULL;

            IF COL_LENGTH(N'dbo.ApplicationStatusHistory', N'PreviousStatus') IS NULL
                ALTER TABLE [dbo].[ApplicationStatusHistory] ADD [PreviousStatus] nvarchar(50) NULL;

            IF COL_LENGTH(N'dbo.PasswordResetTokens', N'AdminID') IS NULL
                ALTER TABLE [dbo].[PasswordResetTokens] ADD [AdminID] int NULL;
            IF COL_LENGTH(N'dbo.PasswordResetTokens', N'CreatedAtUtc') IS NULL
                ALTER TABLE [dbo].[PasswordResetTokens] ADD [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_PasswordResetTokens_CreatedAtUtc] DEFAULT (SYSUTCDATETIME());
            IF COL_LENGTH(N'dbo.PasswordResetTokens', N'RowVersion') IS NULL
                ALTER TABLE [dbo].[PasswordResetTokens] ADD [RowVersion] rowversion NOT NULL;
            """);

        migrationBuilder.Sql(
            """
            EXEC sys.sp_executesql N'UPDATE [dbo].[Admins]
                SET [NormalizedEmail] = UPPER(LTRIM(RTRIM([Email])))
                WHERE [NormalizedEmail] IS NULL OR [NormalizedEmail] = '''';';

            EXEC sys.sp_executesql N'UPDATE [dbo].[Students]
                SET [NormalizedEmail] = UPPER(LTRIM(RTRIM([Email])))
                WHERE [NormalizedEmail] IS NULL OR [NormalizedEmail] = '''';';

            UPDATE [dbo].[Applications]
            SET [CurrentStatus] = N'Pending'
            WHERE [CurrentStatus] IS NULL;

            UPDATE [dbo].[Applications]
            SET [CurrentStatus] = CASE [CurrentStatus]
                WHEN N'Sisteme Alındı' THEN N'Pending'
                WHEN N'Onay Bekliyor' THEN N'Pending'
                WHEN N'İnceleniyor' THEN N'UnderReview'
                WHEN N'Onaylandı' THEN N'Approved'
                WHEN N'Reddedildi' THEN N'Rejected'
                ELSE [CurrentStatus]
            END;

            UPDATE [dbo].[Applications]
            SET [ApplicationDate] = SYSUTCDATETIME()
            WHERE [ApplicationDate] IS NULL;

            UPDATE [dbo].[ApplicationStatusHistory]
            SET [ChangeDate] = SYSUTCDATETIME()
            WHERE [ChangeDate] IS NULL;

            UPDATE [dbo].[PasswordResetTokens]
            SET [IsUsed] = 1
            WHERE [IsUsed] IS NULL;
            """);

        migrationBuilder.Sql(
            """
            EXEC sys.sp_executesql N'IF EXISTS (SELECT 1 FROM [dbo].[Admins] GROUP BY [NormalizedEmail] HAVING COUNT_BIG(*) > 1)
                THROW 51003, ''Yönetici e-posta kayıtları yineleniyor; unique index oluşturulmadı.'', 1;';

            EXEC sys.sp_executesql N'IF EXISTS (SELECT 1 FROM [dbo].[Students] GROUP BY [NormalizedEmail] HAVING COUNT_BIG(*) > 1)
                THROW 51004, ''Öğrenci e-posta kayıtları yineleniyor; unique index oluşturulmadı.'', 1;';

            IF EXISTS (SELECT 1 FROM [dbo].[ApplicationStatusHistory] WHERE LEN([Notes]) > 500)
                THROW 51005, '500 karakteri aşan durum notları var; veri kesilmedi ve migration durduruldu.', 1;
            """);

        // Capture dependencies before ALTER COLUMN. Defaults that are not canonical are restored verbatim.
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'tempdb..#HardenAlterTargets', N'U') IS NOT NULL
                DROP TABLE #HardenAlterTargets;
            IF OBJECT_ID(N'tempdb..#HardenDefaults', N'U') IS NOT NULL
                DROP TABLE #HardenDefaults;
            IF OBJECT_ID(N'tempdb..#HardenForeignKeys', N'U') IS NOT NULL
                DROP TABLE #HardenForeignKeys;
            IF OBJECT_ID(N'tempdb..#HardenIndexColumns', N'U') IS NOT NULL
                DROP TABLE #HardenIndexColumns;
            IF OBJECT_ID(N'tempdb..#HardenIndexes', N'U') IS NOT NULL
                DROP TABLE #HardenIndexes;
            IF OBJECT_ID(N'tempdb..#HardenKeyConstraintColumns', N'U') IS NOT NULL
                DROP TABLE #HardenKeyConstraintColumns;
            IF OBJECT_ID(N'tempdb..#HardenKeyConstraints', N'U') IS NOT NULL
                DROP TABLE #HardenKeyConstraints;
            IF OBJECT_ID(N'tempdb..#HardenExpectedIndexLocations', N'U') IS NOT NULL
                DROP TABLE #HardenExpectedIndexLocations;
            IF OBJECT_ID(N'tempdb..#HardenExpectedIndexColumns', N'U') IS NOT NULL
                DROP TABLE #HardenExpectedIndexColumns;
            IF OBJECT_ID(N'tempdb..#HardenExpectedIndexes', N'U') IS NOT NULL
                DROP TABLE #HardenExpectedIndexes;

            CREATE TABLE #HardenAlterTargets
            (
                [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [TableName] sysname NOT NULL,
                [ColumnName] sysname NOT NULL,
                [HasCanonicalDefault] bit NOT NULL,
                [TargetSystemTypeId] int NOT NULL,
                [TargetMaxLength] smallint NOT NULL,
                [TargetPrecision] tinyint NOT NULL,
                [TargetScale] tinyint NOT NULL,
                [TargetIsNullable] bit NOT NULL,
                [AlterSql] nvarchar(500) NOT NULL,
                [NeedsAlter] bit NOT NULL
            );

            INSERT INTO #HardenAlterTargets
                ([TableName], [ColumnName], [HasCanonicalDefault], [TargetSystemTypeId], [TargetMaxLength],
                 [TargetPrecision], [TargetScale], [TargetIsNullable], [AlterSql], [NeedsAlter])
            VALUES
                (N'Admins', N'NormalizedEmail', 0, 231, 508, 0, 0, 0, N'ALTER TABLE [dbo].[Admins] ALTER COLUMN [NormalizedEmail] nvarchar(254) NOT NULL;', 1),
                (N'Admins', N'Email', 0, 231, 508, 0, 0, 0, N'ALTER TABLE [dbo].[Admins] ALTER COLUMN [Email] nvarchar(254) NOT NULL;', 1),
                (N'Admins', N'PasswordHash', 0, 167, 512, 0, 0, 0, N'ALTER TABLE [dbo].[Admins] ALTER COLUMN [PasswordHash] varchar(512) NOT NULL;', 1),
                (N'Students', N'NormalizedEmail', 0, 231, 508, 0, 0, 0, N'ALTER TABLE [dbo].[Students] ALTER COLUMN [NormalizedEmail] nvarchar(254) NOT NULL;', 1),
                (N'Students', N'Email', 0, 231, 508, 0, 0, 0, N'ALTER TABLE [dbo].[Students] ALTER COLUMN [Email] nvarchar(254) NOT NULL;', 1),
                (N'Students', N'PasswordHash', 0, 167, 512, 0, 0, 0, N'ALTER TABLE [dbo].[Students] ALTER COLUMN [PasswordHash] varchar(512) NOT NULL;', 1),
                (N'Applications', N'ApplicationDate', 1, 42, 8, 27, 7, 0, N'ALTER TABLE [dbo].[Applications] ALTER COLUMN [ApplicationDate] datetime2 NOT NULL;', 1),
                (N'Applications', N'CurrentStatus', 1, 231, 100, 0, 0, 0, N'ALTER TABLE [dbo].[Applications] ALTER COLUMN [CurrentStatus] nvarchar(50) NOT NULL;', 1),
                (N'ApplicationStatusHistory', N'ChangeDate', 1, 42, 8, 27, 7, 0, N'ALTER TABLE [dbo].[ApplicationStatusHistory] ALTER COLUMN [ChangeDate] datetime2 NOT NULL;', 1),
                (N'ApplicationStatusHistory', N'Notes', 0, 231, 1000, 0, 0, 1, N'ALTER TABLE [dbo].[ApplicationStatusHistory] ALTER COLUMN [Notes] nvarchar(500) NULL;', 1),
                (N'PasswordResetTokens', N'TC', 0, 175, 11, 0, 0, 1, N'ALTER TABLE [dbo].[PasswordResetTokens] ALTER COLUMN [TC] char(11) NULL;', 1),
                (N'PasswordResetTokens', N'IsUsed', 1, 104, 1, 1, 0, 0, N'ALTER TABLE [dbo].[PasswordResetTokens] ALTER COLUMN [IsUsed] bit NOT NULL;', 1),
                (N'PasswordResetTokens', N'ExpirationDate', 0, 42, 8, 27, 7, 0, N'ALTER TABLE [dbo].[PasswordResetTokens] ALTER COLUMN [ExpirationDate] datetime2 NOT NULL;', 1);

            UPDATE target
            SET [NeedsAlter] = CASE
                WHEN columnInfo.[object_id] IS NOT NULL
                 AND columnInfo.[system_type_id] = target.[TargetSystemTypeId]
                 AND columnInfo.[user_type_id] = columnInfo.[system_type_id]
                 AND columnInfo.[max_length] = target.[TargetMaxLength]
                 AND columnInfo.[precision] = target.[TargetPrecision]
                 AND columnInfo.[scale] = target.[TargetScale]
                 AND columnInfo.[is_nullable] = target.[TargetIsNullable]
                THEN 0 ELSE 1 END
            FROM #HardenAlterTargets AS target
            LEFT JOIN sys.tables AS tableInfo
                ON tableInfo.[schema_id] = SCHEMA_ID(N'dbo')
               AND tableInfo.[name] = target.[TableName]
            LEFT JOIN sys.columns AS columnInfo
                ON columnInfo.[object_id] = tableInfo.[object_id]
               AND columnInfo.[name] = target.[ColumnName];

            CREATE TABLE #HardenExpectedIndexes
            (
                [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [SchemaName] sysname NOT NULL,
                [TableName] sysname NOT NULL,
                [IndexName] sysname NOT NULL,
                [IsUnique] bit NOT NULL
            );

            INSERT INTO #HardenExpectedIndexes ([SchemaName], [TableName], [IndexName], [IsUnique])
            VALUES
                (N'dbo', N'Admins', N'IX_Admins_NormalizedEmail', 1),
                (N'dbo', N'Students', N'IX_Students_NormalizedEmail', 1),
                (N'dbo', N'Applications', N'IX_Applications_TC_ProgramID', 1),
                (N'dbo', N'PasswordResetTokens', N'IX_PasswordResetTokens_AdminID', 0);

            CREATE TABLE #HardenExpectedIndexColumns
            (
                [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [SchemaName] sysname NOT NULL,
                [TableName] sysname NOT NULL,
                [IndexName] sysname NOT NULL,
                [ColumnName] sysname NOT NULL,
                [KeyOrdinal] tinyint NOT NULL,
                [IsDescending] bit NOT NULL,
                [IsIncluded] bit NOT NULL
            );

            INSERT INTO #HardenExpectedIndexColumns
                ([SchemaName], [TableName], [IndexName], [ColumnName], [KeyOrdinal], [IsDescending], [IsIncluded])
            VALUES
                (N'dbo', N'Admins', N'IX_Admins_NormalizedEmail', N'NormalizedEmail', 1, 0, 0),
                (N'dbo', N'Students', N'IX_Students_NormalizedEmail', N'NormalizedEmail', 1, 0, 0),
                (N'dbo', N'Applications', N'IX_Applications_TC_ProgramID', N'TC', 1, 0, 0),
                (N'dbo', N'Applications', N'IX_Applications_TC_ProgramID', N'ProgramID', 2, 0, 0),
                (N'dbo', N'PasswordResetTokens', N'IX_PasswordResetTokens_AdminID', N'AdminID', 1, 0, 0);

            CREATE TABLE #HardenExpectedIndexLocations
            (
                [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [SchemaName] sysname NOT NULL,
                [TableName] sysname NOT NULL,
                [IndexName] sysname NOT NULL,
                [DataSpaceName] sysname NULL
            );

            DECLARE @invalidExpectedIndex nvarchar(776);
            SELECT TOP (1)
                @invalidExpectedIndex = QUOTENAME(expected.[SchemaName]) + N'.' + QUOTENAME(expected.[TableName]) + N'.' + QUOTENAME(expected.[IndexName])
            FROM #HardenExpectedIndexes AS expected
            INNER JOIN sys.schemas AS schemaInfo
                ON schemaInfo.[name] = expected.[SchemaName]
            INNER JOIN sys.tables AS tableInfo
                ON tableInfo.[schema_id] = schemaInfo.[schema_id]
               AND tableInfo.[name] = expected.[TableName]
            INNER JOIN sys.indexes AS indexInfo
                ON indexInfo.[object_id] = tableInfo.[object_id]
               AND indexInfo.[name] = expected.[IndexName]
            LEFT JOIN sys.data_spaces AS dataSpaceInfo
                ON dataSpaceInfo.[data_space_id] = indexInfo.[data_space_id]
            WHERE indexInfo.[type] <> 2
               OR indexInfo.[is_unique] <> expected.[IsUnique]
               OR indexInfo.[is_primary_key] <> 0
               OR indexInfo.[is_unique_constraint] <> 0
               OR indexInfo.[is_hypothetical] <> 0
               OR indexInfo.[is_disabled] <> 0
               OR indexInfo.[has_filter] <> 0
               OR indexInfo.[filter_definition] IS NOT NULL
               OR indexInfo.[fill_factor] <> 0
               OR indexInfo.[is_padded] <> 0
               OR indexInfo.[ignore_dup_key] <> 0
               OR indexInfo.[allow_row_locks] <> 1
               OR indexInfo.[allow_page_locks] <> 1
               OR dataSpaceInfo.[type] IS NULL
               OR dataSpaceInfo.[type] <> N'FG'
               OR EXISTS
               (
                   SELECT columnInfo.[name], indexColumnInfo.[key_ordinal], indexColumnInfo.[is_descending_key], indexColumnInfo.[is_included_column]
                   FROM sys.index_columns AS indexColumnInfo
                   INNER JOIN sys.columns AS columnInfo
                       ON columnInfo.[object_id] = indexColumnInfo.[object_id]
                      AND columnInfo.[column_id] = indexColumnInfo.[column_id]
                   WHERE indexColumnInfo.[object_id] = indexInfo.[object_id]
                     AND indexColumnInfo.[index_id] = indexInfo.[index_id]
                   EXCEPT
                   SELECT [ColumnName], [KeyOrdinal], [IsDescending], [IsIncluded]
                   FROM #HardenExpectedIndexColumns AS expectedColumn
                   WHERE expectedColumn.[SchemaName] = expected.[SchemaName]
                     AND expectedColumn.[TableName] = expected.[TableName]
                     AND expectedColumn.[IndexName] = expected.[IndexName]
               )
               OR EXISTS
               (
                   SELECT [ColumnName], [KeyOrdinal], [IsDescending], [IsIncluded]
                   FROM #HardenExpectedIndexColumns AS expectedColumn
                   WHERE expectedColumn.[SchemaName] = expected.[SchemaName]
                     AND expectedColumn.[TableName] = expected.[TableName]
                     AND expectedColumn.[IndexName] = expected.[IndexName]
                   EXCEPT
                   SELECT columnInfo.[name], indexColumnInfo.[key_ordinal], indexColumnInfo.[is_descending_key], indexColumnInfo.[is_included_column]
                   FROM sys.index_columns AS indexColumnInfo
                   INNER JOIN sys.columns AS columnInfo
                       ON columnInfo.[object_id] = indexColumnInfo.[object_id]
                      AND columnInfo.[column_id] = indexColumnInfo.[column_id]
                   WHERE indexColumnInfo.[object_id] = indexInfo.[object_id]
                     AND indexColumnInfo.[index_id] = indexInfo.[index_id]
               )
            ORDER BY expected.[SchemaName], expected.[TableName], expected.[IndexName];

            IF @invalidExpectedIndex IS NOT NULL
            BEGIN
                DECLARE @invalidExpectedIndexMessage nvarchar(2048) = N'Migration tarafindan yonetilen index mevcut fakat beklenen imzayla uyusmuyor: ' + @invalidExpectedIndex + N'. Migration durduruldu.';
                THROW 51010, @invalidExpectedIndexMessage, 1;
            END;

            INSERT INTO #HardenExpectedIndexLocations ([SchemaName], [TableName], [IndexName], [DataSpaceName])
            SELECT expected.[SchemaName], expected.[TableName], expected.[IndexName], dataSpaceInfo.[name]
            FROM #HardenExpectedIndexes AS expected
            INNER JOIN sys.schemas AS schemaInfo
                ON schemaInfo.[name] = expected.[SchemaName]
            INNER JOIN sys.tables AS tableInfo
                ON tableInfo.[schema_id] = schemaInfo.[schema_id]
               AND tableInfo.[name] = expected.[TableName]
            INNER JOIN sys.indexes AS indexInfo
                ON indexInfo.[object_id] = tableInfo.[object_id]
               AND indexInfo.[name] = expected.[IndexName]
            INNER JOIN sys.data_spaces AS dataSpaceInfo
                ON dataSpaceInfo.[data_space_id] = indexInfo.[data_space_id];

            CREATE TABLE #HardenDefaults
            (
                [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [SchemaName] sysname NOT NULL,
                [TableName] sysname NOT NULL,
                [ColumnName] sysname NOT NULL,
                [ConstraintName] sysname NOT NULL,
                [Definition] nvarchar(max) NOT NULL,
                [HasCanonicalDefault] bit NOT NULL
            );

            INSERT INTO #HardenDefaults
                ([SchemaName], [TableName], [ColumnName], [ConstraintName], [Definition], [HasCanonicalDefault])
            SELECT
                schemaInfo.[name],
                tableInfo.[name],
                columnInfo.[name],
                defaultInfo.[name],
                defaultInfo.[definition],
                target.[HasCanonicalDefault]
            FROM sys.default_constraints AS defaultInfo
            INNER JOIN sys.columns AS columnInfo
                ON columnInfo.[object_id] = defaultInfo.[parent_object_id]
               AND columnInfo.[column_id] = defaultInfo.[parent_column_id]
            INNER JOIN sys.tables AS tableInfo
                ON tableInfo.[object_id] = defaultInfo.[parent_object_id]
            INNER JOIN sys.schemas AS schemaInfo
                ON schemaInfo.[schema_id] = tableInfo.[schema_id]
            INNER JOIN #HardenAlterTargets AS target
                ON target.[TableName] = tableInfo.[name]
               AND target.[ColumnName] = columnInfo.[name]
            WHERE schemaInfo.[name] = N'dbo'
              AND (target.[NeedsAlter] = 1 OR target.[HasCanonicalDefault] = 1);

            CREATE TABLE #HardenForeignKeys
            (
                [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [ConstraintName] sysname NOT NULL,
                [ParentSchema] sysname NOT NULL,
                [ParentTable] sysname NOT NULL,
                [ParentColumn] sysname NOT NULL,
                [ReferencedSchema] sysname NOT NULL,
                [ReferencedTable] sysname NOT NULL,
                [ReferencedColumn] sysname NOT NULL,
                [DeleteAction] tinyint NOT NULL,
                [UpdateAction] tinyint NOT NULL,
                [IsNotForReplication] bit NOT NULL,
                [IsDisabled] bit NOT NULL,
                [IsNotTrusted] bit NOT NULL
            );

            IF EXISTS (SELECT 1 FROM #HardenAlterTargets WHERE [TableName] = N'PasswordResetTokens' AND [ColumnName] = N'TC' AND [NeedsAlter] = 1)
               AND EXISTS
            (
                SELECT foreignKeyInfo.[object_id]
                FROM sys.foreign_keys AS foreignKeyInfo
                WHERE EXISTS
                (
                    SELECT 1
                    FROM sys.foreign_key_columns AS targetColumnInfo
                    INNER JOIN sys.columns AS targetParentColumn
                        ON targetParentColumn.[object_id] = targetColumnInfo.[parent_object_id]
                       AND targetParentColumn.[column_id] = targetColumnInfo.[parent_column_id]
                    WHERE targetColumnInfo.[constraint_object_id] = foreignKeyInfo.[object_id]
                      AND targetColumnInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[PasswordResetTokens]')
                      AND targetParentColumn.[name] = N'TC'
                )
                GROUP BY foreignKeyInfo.[object_id]
                HAVING (SELECT COUNT_BIG(*) FROM sys.foreign_key_columns AS allColumns WHERE allColumns.[constraint_object_id] = foreignKeyInfo.[object_id]) <> 1
            )
                THROW 51006, 'PasswordResetTokens.TC üzerinde bileşik foreign key bulundu. Veri kaybını önlemek için migration durduruldu.', 1;

            INSERT INTO #HardenForeignKeys
                ([ConstraintName], [ParentSchema], [ParentTable], [ParentColumn], [ReferencedSchema], [ReferencedTable], [ReferencedColumn],
                 [DeleteAction], [UpdateAction], [IsNotForReplication], [IsDisabled], [IsNotTrusted])
            SELECT
                foreignKeyInfo.[name],
                parentSchema.[name],
                parentTable.[name],
                parentColumn.[name],
                referencedSchema.[name],
                referencedTable.[name],
                referencedColumn.[name],
                foreignKeyInfo.[delete_referential_action],
                foreignKeyInfo.[update_referential_action],
                foreignKeyInfo.[is_not_for_replication],
                foreignKeyInfo.[is_disabled],
                foreignKeyInfo.[is_not_trusted]
            FROM sys.foreign_keys AS foreignKeyInfo
            INNER JOIN sys.foreign_key_columns AS foreignKeyColumn
                ON foreignKeyColumn.[constraint_object_id] = foreignKeyInfo.[object_id]
            INNER JOIN sys.tables AS parentTable
                ON parentTable.[object_id] = foreignKeyColumn.[parent_object_id]
            INNER JOIN sys.schemas AS parentSchema
                ON parentSchema.[schema_id] = parentTable.[schema_id]
            INNER JOIN sys.columns AS parentColumn
                ON parentColumn.[object_id] = foreignKeyColumn.[parent_object_id]
               AND parentColumn.[column_id] = foreignKeyColumn.[parent_column_id]
            INNER JOIN sys.tables AS referencedTable
                ON referencedTable.[object_id] = foreignKeyColumn.[referenced_object_id]
            INNER JOIN sys.schemas AS referencedSchema
                ON referencedSchema.[schema_id] = referencedTable.[schema_id]
            INNER JOIN sys.columns AS referencedColumn
                ON referencedColumn.[object_id] = foreignKeyColumn.[referenced_object_id]
               AND referencedColumn.[column_id] = foreignKeyColumn.[referenced_column_id]
            WHERE foreignKeyColumn.[parent_object_id] = OBJECT_ID(N'[dbo].[PasswordResetTokens]')
              AND parentColumn.[name] = N'TC'
              AND EXISTS (SELECT 1 FROM #HardenAlterTargets WHERE [TableName] = N'PasswordResetTokens' AND [ColumnName] = N'TC' AND [NeedsAlter] = 1);

            CREATE TABLE #HardenKeyConstraints
            (
                [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [ConstraintObjectId] int NOT NULL,
                [ParentObjectId] int NOT NULL,
                [ConstraintName] sysname NOT NULL,
                [ConstraintType] char(2) NOT NULL,
                [SchemaName] sysname NOT NULL,
                [TableName] sysname NOT NULL,
                [IndexType] tinyint NOT NULL,
                [IndexTypeDescription] nvarchar(60) NOT NULL,
                [FillFactor] tinyint NOT NULL,
                [IsPadded] bit NOT NULL,
                [IgnoreDuplicateKey] bit NOT NULL,
                [AllowRowLocks] bit NOT NULL,
                [AllowPageLocks] bit NOT NULL,
                [IsDisabled] bit NOT NULL,
                [HasFilter] bit NOT NULL,
                [DataSpaceName] sysname NULL,
                [DataSpaceType] nvarchar(2) NULL,
                [InboundForeignKeyCount] bigint NOT NULL,
                [IsAlterTargetRelated] bit NOT NULL
            );

            INSERT INTO #HardenKeyConstraints
                ([ConstraintObjectId], [ParentObjectId], [ConstraintName], [ConstraintType], [SchemaName], [TableName],
                 [IndexType], [IndexTypeDescription], [FillFactor], [IsPadded], [IgnoreDuplicateKey], [AllowRowLocks],
                 [AllowPageLocks], [IsDisabled], [HasFilter], [DataSpaceName], [DataSpaceType], [InboundForeignKeyCount],
                 [IsAlterTargetRelated])
            SELECT
                keyConstraint.[object_id],
                keyConstraint.[parent_object_id],
                keyConstraint.[name],
                keyConstraint.[type],
                schemaInfo.[name],
                tableInfo.[name],
                indexInfo.[type],
                indexInfo.[type_desc],
                indexInfo.[fill_factor],
                indexInfo.[is_padded],
                indexInfo.[ignore_dup_key],
                indexInfo.[allow_row_locks],
                indexInfo.[allow_page_locks],
                indexInfo.[is_disabled],
                indexInfo.[has_filter],
                dataSpaceInfo.[name],
                dataSpaceInfo.[type],
                (
                    SELECT COUNT_BIG(*)
                    FROM sys.foreign_keys AS inboundForeignKey
                    WHERE inboundForeignKey.[referenced_object_id] = keyConstraint.[parent_object_id]
                      AND inboundForeignKey.[key_index_id] = keyConstraint.[unique_index_id]
                ),
                CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM sys.index_columns AS relatedIndexColumn
                    INNER JOIN sys.columns AS relatedColumn
                        ON relatedColumn.[object_id] = relatedIndexColumn.[object_id]
                       AND relatedColumn.[column_id] = relatedIndexColumn.[column_id]
                    INNER JOIN #HardenAlterTargets AS relatedTarget
                        ON relatedTarget.[TableName] = tableInfo.[name]
                       AND relatedTarget.[ColumnName] = relatedColumn.[name]
                       AND relatedTarget.[NeedsAlter] = 1
                    WHERE relatedIndexColumn.[object_id] = keyConstraint.[parent_object_id]
                      AND relatedIndexColumn.[index_id] = keyConstraint.[unique_index_id]
                      AND relatedIndexColumn.[key_ordinal] > 0
                ) THEN 1 ELSE 0 END
            FROM sys.key_constraints AS keyConstraint
            INNER JOIN sys.tables AS tableInfo
                ON tableInfo.[object_id] = keyConstraint.[parent_object_id]
            INNER JOIN sys.schemas AS schemaInfo
                ON schemaInfo.[schema_id] = tableInfo.[schema_id]
            INNER JOIN sys.indexes AS indexInfo
                ON indexInfo.[object_id] = keyConstraint.[parent_object_id]
               AND indexInfo.[index_id] = keyConstraint.[unique_index_id]
            LEFT JOIN sys.data_spaces AS dataSpaceInfo
                ON dataSpaceInfo.[data_space_id] = indexInfo.[data_space_id]
            WHERE schemaInfo.[name] = N'dbo'
              AND EXISTS
              (
                  SELECT 1
                  FROM #HardenAlterTargets AS tableTarget
                  WHERE tableTarget.[TableName] = tableInfo.[name]
              );

            CREATE TABLE #HardenKeyConstraintColumns
            (
                [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [KeyConstraintId] bigint NOT NULL,
                [ColumnName] sysname NOT NULL,
                [KeyOrdinal] tinyint NOT NULL,
                [IsDescending] bit NOT NULL
            );

            INSERT INTO #HardenKeyConstraintColumns
                ([KeyConstraintId], [ColumnName], [KeyOrdinal], [IsDescending])
            SELECT
                capturedConstraint.[Id],
                columnInfo.[name],
                indexColumnInfo.[key_ordinal],
                indexColumnInfo.[is_descending_key]
            FROM #HardenKeyConstraints AS capturedConstraint
            INNER JOIN sys.key_constraints AS keyConstraint
                ON keyConstraint.[object_id] = capturedConstraint.[ConstraintObjectId]
            INNER JOIN sys.index_columns AS indexColumnInfo
                ON indexColumnInfo.[object_id] = keyConstraint.[parent_object_id]
               AND indexColumnInfo.[index_id] = keyConstraint.[unique_index_id]
               AND indexColumnInfo.[key_ordinal] > 0
            INNER JOIN sys.columns AS columnInfo
                ON columnInfo.[object_id] = indexColumnInfo.[object_id]
               AND columnInfo.[column_id] = indexColumnInfo.[column_id];

            DECLARE @unsafePrimaryKey nvarchar(776);
            DECLARE @unsafePrimaryKeyInboundForeignKeys bigint;
            SELECT TOP (1)
                @unsafePrimaryKey = QUOTENAME([SchemaName]) + N'.' + QUOTENAME([TableName]) + N'.' + QUOTENAME([ConstraintName]),
                @unsafePrimaryKeyInboundForeignKeys = [InboundForeignKeyCount]
            FROM #HardenKeyConstraints
            WHERE [ConstraintType] = N'PK'
              AND [IsAlterTargetRelated] = 1
            ORDER BY [SchemaName], [TableName], [ConstraintName];

            IF @unsafePrimaryKey IS NOT NULL
            BEGIN
                DECLARE @unsafePrimaryKeyMessage nvarchar(2048) = N'ALTER COLUMN hedefinde desteklenmeyen PRIMARY KEY bagimliligi bulundu: '
                    + @unsafePrimaryKey + N'. Inbound foreign key sayisi: ' + CONVERT(nvarchar(20), @unsafePrimaryKeyInboundForeignKeys)
                    + N'. PRIMARY KEY ve referans semantigi eksiksiz korunamadigi icin migration durduruldu.';
                THROW 51007, @unsafePrimaryKeyMessage, 1;
            END;

            DECLARE @referencedUniqueConstraint nvarchar(776);
            SELECT TOP (1)
                @referencedUniqueConstraint = QUOTENAME([SchemaName]) + N'.' + QUOTENAME([TableName]) + N'.' + QUOTENAME([ConstraintName])
            FROM #HardenKeyConstraints
            WHERE [ConstraintType] = N'UQ'
              AND [IsAlterTargetRelated] = 1
              AND [InboundForeignKeyCount] > 0
            ORDER BY [SchemaName], [TableName], [ConstraintName];

            IF @referencedUniqueConstraint IS NOT NULL
            BEGIN
                DECLARE @referencedUniqueConstraintMessage nvarchar(2048) = N'ALTER COLUMN hedefindeki UNIQUE constraint inbound foreign key tarafindan kullaniliyor: '
                    + @referencedUniqueConstraint + N'. Foreign key semantigi desteklenmedigi icin migration durduruldu.';
                THROW 51014, @referencedUniqueConstraintMessage, 1;
            END;

            DECLARE @unsupportedUniqueConstraint nvarchar(776);
            SELECT TOP (1)
                @unsupportedUniqueConstraint = QUOTENAME([SchemaName]) + N'.' + QUOTENAME([TableName]) + N'.' + QUOTENAME([ConstraintName])
            FROM #HardenKeyConstraints
            WHERE [ConstraintType] = N'UQ'
              AND [IsAlterTargetRelated] = 1
              AND
              (
                  [IndexType] NOT IN (1, 2)
                  OR [IsDisabled] = 1
                  OR [HasFilter] = 1
                  OR [DataSpaceName] IS NULL
                  OR [DataSpaceType] <> N'FG'
              )
            ORDER BY [SchemaName], [TableName], [ConstraintName];

            IF @unsupportedUniqueConstraint IS NOT NULL
            BEGIN
                DECLARE @unsupportedUniqueConstraintMessage nvarchar(2048) = N'ALTER COLUMN hedefindeki UNIQUE constraint guvenle yeniden olusturulamiyor: '
                    + @unsupportedUniqueConstraint + N'. Migration durduruldu.';
                THROW 51015, @unsupportedUniqueConstraintMessage, 1;
            END;

            CREATE TABLE #HardenIndexes
            (
                [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [SchemaName] sysname NOT NULL,
                [TableName] sysname NOT NULL,
                [IndexName] sysname NOT NULL,
                [IndexType] tinyint NOT NULL,
                [IndexTypeDescription] nvarchar(60) NOT NULL,
                [IsUnique] bit NOT NULL,
                [IsPrimaryKey] bit NOT NULL,
                [IsUniqueConstraint] bit NOT NULL,
                [IsDisabled] bit NOT NULL,
                [FillFactor] tinyint NOT NULL,
                [IsPadded] bit NOT NULL,
                [IgnoreDuplicateKey] bit NOT NULL,
                [AllowRowLocks] bit NOT NULL,
                [AllowPageLocks] bit NOT NULL,
                [HasFilter] bit NOT NULL,
                [FilterDefinition] nvarchar(max) NULL,
                [DataSpaceName] sysname NULL,
                [DataSpaceType] nvarchar(2) NULL
            );

            INSERT INTO #HardenIndexes
                ([SchemaName], [TableName], [IndexName], [IndexType], [IndexTypeDescription], [IsUnique],
                 [IsPrimaryKey], [IsUniqueConstraint], [IsDisabled], [FillFactor], [IsPadded], [IgnoreDuplicateKey],
                 [AllowRowLocks], [AllowPageLocks], [HasFilter], [FilterDefinition], [DataSpaceName], [DataSpaceType])
            SELECT
                indexedSchema.[name],
                indexedTable.[name],
                indexInfo.[name],
                indexInfo.[type],
                indexInfo.[type_desc],
                indexInfo.[is_unique],
                indexInfo.[is_primary_key],
                indexInfo.[is_unique_constraint],
                indexInfo.[is_disabled],
                indexInfo.[fill_factor],
                indexInfo.[is_padded],
                indexInfo.[ignore_dup_key],
                indexInfo.[allow_row_locks],
                indexInfo.[allow_page_locks],
                indexInfo.[has_filter],
                indexInfo.[filter_definition],
                dataSpaceInfo.[name],
                dataSpaceInfo.[type]
            FROM sys.indexes AS indexInfo
            INNER JOIN sys.tables AS indexedTable
                ON indexedTable.[object_id] = indexInfo.[object_id]
            INNER JOIN sys.schemas AS indexedSchema
                ON indexedSchema.[schema_id] = indexedTable.[schema_id]
            LEFT JOIN sys.data_spaces AS dataSpaceInfo
                ON dataSpaceInfo.[data_space_id] = indexInfo.[data_space_id]
            WHERE indexedSchema.[name] = N'dbo'
              AND indexInfo.[index_id] > 0
              AND indexInfo.[is_hypothetical] = 0
              AND indexInfo.[is_primary_key] = 0
              AND indexInfo.[is_unique_constraint] = 0
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM #HardenExpectedIndexes AS expected
                  WHERE expected.[SchemaName] = indexedSchema.[name]
                    AND expected.[TableName] = indexedTable.[name]
                    AND expected.[IndexName] = indexInfo.[name]
              )
              AND
              (
                  EXISTS
                  (
                      SELECT 1
                      FROM sys.index_columns AS dependencyColumnInfo
                      INNER JOIN sys.columns AS dependencyColumn
                          ON dependencyColumn.[object_id] = dependencyColumnInfo.[object_id]
                         AND dependencyColumn.[column_id] = dependencyColumnInfo.[column_id]
                      INNER JOIN #HardenAlterTargets AS target
                          ON target.[TableName] = indexedTable.[name]
                         AND target.[ColumnName] = dependencyColumn.[name]
                         AND target.[NeedsAlter] = 1
                      WHERE dependencyColumnInfo.[object_id] = indexInfo.[object_id]
                        AND dependencyColumnInfo.[index_id] = indexInfo.[index_id]
                  )
                  OR
                  (
                      indexInfo.[has_filter] = 1
                      AND EXISTS
                      (
                          SELECT 1
                          FROM #HardenAlterTargets AS filteredTableTarget
                          WHERE filteredTableTarget.[TableName] = indexedTable.[name]
                            AND filteredTableTarget.[NeedsAlter] = 1
                      )
                  )
              );

            DECLARE @unsupportedIndex nvarchar(776);
            DECLARE @unsupportedIndexType nvarchar(60);
            SELECT TOP (1)
                @unsupportedIndex = QUOTENAME([SchemaName]) + N'.' + QUOTENAME([TableName]) + N'.' + QUOTENAME([IndexName]),
                @unsupportedIndexType = [IndexTypeDescription]
            FROM #HardenIndexes
            WHERE [IndexType] <> 2
            ORDER BY [SchemaName], [TableName], [IndexName];

            IF @unsupportedIndex IS NOT NULL
            BEGIN
                DECLARE @unsupportedIndexMessage nvarchar(2048) = N'ALTER COLUMN hedefinde desteklenmeyen index turu bulundu: ' + @unsupportedIndex + N' (' + @unsupportedIndexType + N'). Migration durduruldu.';
                THROW 51011, @unsupportedIndexMessage, 1;
            END;

            DECLARE @unsupportedDataSpaceIndex nvarchar(776);
            SELECT TOP (1)
                @unsupportedDataSpaceIndex = QUOTENAME([SchemaName]) + N'.' + QUOTENAME([TableName]) + N'.' + QUOTENAME([IndexName])
            FROM #HardenIndexes
            WHERE [DataSpaceName] IS NULL OR [DataSpaceType] <> N'FG'
            ORDER BY [SchemaName], [TableName], [IndexName];

            IF @unsupportedDataSpaceIndex IS NOT NULL
            BEGIN
                DECLARE @unsupportedDataSpaceMessage nvarchar(2048) = N'ALTER COLUMN hedefindeki index guvenle yeniden olusturulamayan bir data space kullaniyor: ' + @unsupportedDataSpaceIndex + N'. Migration durduruldu.';
                THROW 51012, @unsupportedDataSpaceMessage, 1;
            END;

            CREATE TABLE #HardenIndexColumns
            (
                [Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [SchemaName] sysname NOT NULL,
                [TableName] sysname NOT NULL,
                [IndexName] sysname NOT NULL,
                [ColumnName] sysname NOT NULL,
                [KeyOrdinal] tinyint NOT NULL,
                [IsDescending] bit NOT NULL,
                [IsIncluded] bit NOT NULL,
                [IndexColumnId] int NOT NULL
            );

            INSERT INTO #HardenIndexColumns
                ([SchemaName], [TableName], [IndexName], [ColumnName], [KeyOrdinal], [IsDescending], [IsIncluded], [IndexColumnId])
            SELECT
                captured.[SchemaName],
                captured.[TableName],
                captured.[IndexName],
                columnInfo.[name],
                indexColumnInfo.[key_ordinal],
                indexColumnInfo.[is_descending_key],
                indexColumnInfo.[is_included_column],
                indexColumnInfo.[index_column_id]
            FROM #HardenIndexes AS captured
            INNER JOIN sys.schemas AS schemaInfo
                ON schemaInfo.[name] = captured.[SchemaName]
            INNER JOIN sys.tables AS tableInfo
                ON tableInfo.[schema_id] = schemaInfo.[schema_id]
               AND tableInfo.[name] = captured.[TableName]
            INNER JOIN sys.indexes AS indexInfo
                ON indexInfo.[object_id] = tableInfo.[object_id]
               AND indexInfo.[name] = captured.[IndexName]
            INNER JOIN sys.index_columns AS indexColumnInfo
                ON indexColumnInfo.[object_id] = indexInfo.[object_id]
               AND indexColumnInfo.[index_id] = indexInfo.[index_id]
            INNER JOIN sys.columns AS columnInfo
                ON columnInfo.[object_id] = indexColumnInfo.[object_id]
               AND columnInfo.[column_id] = indexColumnInfo.[column_id];

            IF EXISTS
            (
                SELECT 1
                FROM sys.check_constraints AS checkInfo
                INNER JOIN sys.tables AS checkedTable
                    ON checkedTable.[object_id] = checkInfo.[parent_object_id]
                INNER JOIN sys.schemas AS checkedSchema
                    ON checkedSchema.[schema_id] = checkedTable.[schema_id]
                INNER JOIN #HardenAlterTargets AS target
                    ON target.[TableName] = checkedTable.[name]
                   AND target.[NeedsAlter] = 1
                INNER JOIN sys.columns AS checkedColumn
                    ON checkedColumn.[object_id] = checkedTable.[object_id]
                   AND checkedColumn.[name] = target.[ColumnName]
                WHERE checkedSchema.[name] = N'dbo'
                  AND
                  (
                      checkInfo.[parent_column_id] = checkedColumn.[column_id]
                      OR EXISTS
                      (
                          SELECT 1
                          FROM sys.sql_expression_dependencies AS dependencyInfo
                          WHERE dependencyInfo.[referencing_id] = checkInfo.[object_id]
                            AND dependencyInfo.[referenced_id] = checkedColumn.[object_id]
                            AND dependencyInfo.[referenced_minor_id] = checkedColumn.[column_id]
                      )
                  )
                  AND NOT (checkedTable.[name] = N'Applications' AND checkedColumn.[name] = N'CurrentStatus')
                  AND NOT (checkedTable.[name] = N'PasswordResetTokens' AND checkInfo.[name] = N'CK_PasswordResetTokens_Subject')
            )
                THROW 51008, 'ALTER COLUMN hedeflerinden birinde güvenle yeniden üretilemeyen check constraint bağımlılığı bulundu. Migration durduruldu.', 1;

            IF EXISTS
            (
                SELECT 1
                FROM sys.foreign_key_columns AS foreignKeyColumn
                INNER JOIN sys.tables AS parentTable
                    ON parentTable.[object_id] = foreignKeyColumn.[parent_object_id]
                INNER JOIN sys.columns AS parentColumn
                    ON parentColumn.[object_id] = foreignKeyColumn.[parent_object_id]
                   AND parentColumn.[column_id] = foreignKeyColumn.[parent_column_id]
                INNER JOIN sys.tables AS referencedTable
                    ON referencedTable.[object_id] = foreignKeyColumn.[referenced_object_id]
                INNER JOIN sys.columns AS referencedColumn
                    ON referencedColumn.[object_id] = foreignKeyColumn.[referenced_object_id]
                   AND referencedColumn.[column_id] = foreignKeyColumn.[referenced_column_id]
                WHERE
                (
                    EXISTS
                    (
                        SELECT 1
                        FROM #HardenAlterTargets AS parentTarget
                        WHERE parentTable.[schema_id] = SCHEMA_ID(N'dbo')
                          AND parentTarget.[TableName] = parentTable.[name]
                          AND parentTarget.[ColumnName] = parentColumn.[name]
                          AND parentTarget.[NeedsAlter] = 1
                    )
                    OR EXISTS
                    (
                        SELECT 1
                        FROM #HardenAlterTargets AS referencedTarget
                        WHERE referencedTable.[schema_id] = SCHEMA_ID(N'dbo')
                          AND referencedTarget.[TableName] = referencedTable.[name]
                          AND referencedTarget.[ColumnName] = referencedColumn.[name]
                          AND referencedTarget.[NeedsAlter] = 1
                    )
                )
                  AND NOT (foreignKeyColumn.[parent_object_id] = OBJECT_ID(N'[dbo].[PasswordResetTokens]') AND parentColumn.[name] = N'TC')
            )
                THROW 51009, 'ALTER COLUMN hedeflerinden birinde güvenle yeniden üretilemeyen foreign key bağımlılığı bulundu. Migration durduruldu.', 1;
            """);

        migrationBuilder.Sql(
            """
            IF EXISTS (SELECT 1 FROM #HardenAlterTargets WHERE [TableName] = N'Admins' AND [ColumnName] = N'NormalizedEmail' AND [NeedsAlter] = 1)
               AND EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Admins]') AND [name] = N'IX_Admins_NormalizedEmail')
                EXEC sys.sp_executesql N'DROP INDEX [IX_Admins_NormalizedEmail] ON [dbo].[Admins];';

            IF EXISTS (SELECT 1 FROM #HardenAlterTargets WHERE [TableName] = N'Students' AND [ColumnName] = N'NormalizedEmail' AND [NeedsAlter] = 1)
               AND EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Students]') AND [name] = N'IX_Students_NormalizedEmail')
                EXEC sys.sp_executesql N'DROP INDEX [IX_Students_NormalizedEmail] ON [dbo].[Students];';

            DECLARE @dropIndexSchema sysname;
            DECLARE @dropIndexTable sysname;
            DECLARE @dropIndexName sysname;
            DECLARE @dropIndexSql nvarchar(max);
            DECLARE harden_index_drop_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT [SchemaName], [TableName], [IndexName]
                FROM #HardenIndexes
                ORDER BY [SchemaName], [TableName], [IndexName];

            OPEN harden_index_drop_cursor;
            FETCH NEXT FROM harden_index_drop_cursor INTO @dropIndexSchema, @dropIndexTable, @dropIndexName;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @dropIndexSql = N'DROP INDEX ' + QUOTENAME(@dropIndexName) + N' ON '
                    + QUOTENAME(@dropIndexSchema) + N'.' + QUOTENAME(@dropIndexTable) + N';';
                EXEC sys.sp_executesql @dropIndexSql;
                FETCH NEXT FROM harden_index_drop_cursor INTO @dropIndexSchema, @dropIndexTable, @dropIndexName;
            END;
            CLOSE harden_index_drop_cursor;
            DEALLOCATE harden_index_drop_cursor;

            DECLARE @dropUniqueConstraintSchema sysname;
            DECLARE @dropUniqueConstraintTable sysname;
            DECLARE @dropUniqueConstraintName sysname;
            DECLARE @dropUniqueConstraintSql nvarchar(max);
            DECLARE harden_unique_constraint_drop_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT [SchemaName], [TableName], [ConstraintName]
                FROM #HardenKeyConstraints
                WHERE [ConstraintType] = N'UQ'
                  AND [IsAlterTargetRelated] = 1
                ORDER BY [SchemaName], [TableName], [ConstraintName];

            OPEN harden_unique_constraint_drop_cursor;
            FETCH NEXT FROM harden_unique_constraint_drop_cursor
                INTO @dropUniqueConstraintSchema, @dropUniqueConstraintTable, @dropUniqueConstraintName;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @dropUniqueConstraintSql = N'ALTER TABLE '
                    + QUOTENAME(@dropUniqueConstraintSchema) + N'.' + QUOTENAME(@dropUniqueConstraintTable)
                    + N' DROP CONSTRAINT ' + QUOTENAME(@dropUniqueConstraintName) + N';';
                EXEC sys.sp_executesql @dropUniqueConstraintSql;
                FETCH NEXT FROM harden_unique_constraint_drop_cursor
                    INTO @dropUniqueConstraintSchema, @dropUniqueConstraintTable, @dropUniqueConstraintName;
            END;
            CLOSE harden_unique_constraint_drop_cursor;
            DEALLOCATE harden_unique_constraint_drop_cursor;

            DECLARE @dropForeignKeySchema sysname;
            DECLARE @dropForeignKeyTable sysname;
            DECLARE @dropForeignKeyConstraint sysname;
            DECLARE @dropForeignKeySql nvarchar(max);
            DECLARE harden_foreign_key_drop_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT [ParentSchema], [ParentTable], [ConstraintName]
                FROM #HardenForeignKeys;

            OPEN harden_foreign_key_drop_cursor;
            FETCH NEXT FROM harden_foreign_key_drop_cursor INTO @dropForeignKeySchema, @dropForeignKeyTable, @dropForeignKeyConstraint;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @dropForeignKeySql = N'ALTER TABLE '
                    + QUOTENAME(@dropForeignKeySchema) + N'.' + QUOTENAME(@dropForeignKeyTable)
                    + N' DROP CONSTRAINT ' + QUOTENAME(@dropForeignKeyConstraint) + N';';
                EXEC sys.sp_executesql @dropForeignKeySql;
                FETCH NEXT FROM harden_foreign_key_drop_cursor INTO @dropForeignKeySchema, @dropForeignKeyTable, @dropForeignKeyConstraint;
            END;
            CLOSE harden_foreign_key_drop_cursor;
            DEALLOCATE harden_foreign_key_drop_cursor;

            DECLARE @dropDefaultSchema sysname;
            DECLARE @dropDefaultTable sysname;
            DECLARE @dropDefaultConstraint sysname;
            DECLARE @dropDefaultSql nvarchar(max);
            DECLARE harden_default_drop_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT [SchemaName], [TableName], [ConstraintName]
                FROM #HardenDefaults;

            OPEN harden_default_drop_cursor;
            FETCH NEXT FROM harden_default_drop_cursor INTO @dropDefaultSchema, @dropDefaultTable, @dropDefaultConstraint;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @dropDefaultSql = N'ALTER TABLE '
                    + QUOTENAME(@dropDefaultSchema) + N'.' + QUOTENAME(@dropDefaultTable)
                    + N' DROP CONSTRAINT ' + QUOTENAME(@dropDefaultConstraint) + N';';
                EXEC sys.sp_executesql @dropDefaultSql;
                FETCH NEXT FROM harden_default_drop_cursor INTO @dropDefaultSchema, @dropDefaultTable, @dropDefaultConstraint;
            END;
            CLOSE harden_default_drop_cursor;
            DEALLOCATE harden_default_drop_cursor;

            DECLARE @dropCheckConstraint sysname;
            DECLARE @dropCheckSql nvarchar(max);
            DECLARE harden_current_status_check_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT DISTINCT checkInfo.[name]
                FROM sys.check_constraints AS checkInfo
                INNER JOIN sys.columns AS checkedColumn
                    ON checkedColumn.[object_id] = checkInfo.[parent_object_id]
                   AND checkedColumn.[name] = N'CurrentStatus'
                WHERE checkInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[Applications]')
                  AND
                  (
                      checkInfo.[parent_column_id] = checkedColumn.[column_id]
                      OR EXISTS
                      (
                          SELECT 1
                          FROM sys.sql_expression_dependencies AS dependencyInfo
                          WHERE dependencyInfo.[referencing_id] = checkInfo.[object_id]
                            AND dependencyInfo.[referenced_id] = checkedColumn.[object_id]
                            AND dependencyInfo.[referenced_minor_id] = checkedColumn.[column_id]
                      )
                  );

            OPEN harden_current_status_check_cursor;
            FETCH NEXT FROM harden_current_status_check_cursor INTO @dropCheckConstraint;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @dropCheckSql = N'ALTER TABLE [dbo].[Applications] DROP CONSTRAINT ' + QUOTENAME(@dropCheckConstraint) + N';';
                EXEC sys.sp_executesql @dropCheckSql;
                FETCH NEXT FROM harden_current_status_check_cursor INTO @dropCheckConstraint;
            END;
            CLOSE harden_current_status_check_cursor;
            DEALLOCATE harden_current_status_check_cursor;

            IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[PasswordResetTokens]') AND [name] = N'CK_PasswordResetTokens_Subject')
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[PasswordResetTokens] DROP CONSTRAINT [CK_PasswordResetTokens_Subject];';
            """);

        migrationBuilder.Sql(
            """
            DECLARE @alterSql nvarchar(500);
            DECLARE harden_alter_column_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT [AlterSql]
                FROM #HardenAlterTargets
                WHERE [NeedsAlter] = 1
                ORDER BY [TableName], [ColumnName];

            OPEN harden_alter_column_cursor;
            FETCH NEXT FROM harden_alter_column_cursor INTO @alterSql;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                EXEC sys.sp_executesql @alterSql;
                FETCH NEXT FROM harden_alter_column_cursor INTO @alterSql;
            END;
            CLOSE harden_alter_column_cursor;
            DEALLOCATE harden_alter_column_cursor;
            """);

        migrationBuilder.Sql(
            """
            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.default_constraints AS defaultInfo
                INNER JOIN sys.columns AS columnInfo
                    ON columnInfo.[object_id] = defaultInfo.[parent_object_id]
                   AND columnInfo.[column_id] = defaultInfo.[parent_column_id]
                WHERE defaultInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[Applications]')
                  AND columnInfo.[name] = N'ApplicationDate'
            )
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Applications] ADD CONSTRAINT [DF_Applications_ApplicationDate] DEFAULT (SYSUTCDATETIME()) FOR [ApplicationDate];';

            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.default_constraints AS defaultInfo
                INNER JOIN sys.columns AS columnInfo
                    ON columnInfo.[object_id] = defaultInfo.[parent_object_id]
                   AND columnInfo.[column_id] = defaultInfo.[parent_column_id]
                WHERE defaultInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[Applications]')
                  AND columnInfo.[name] = N'CurrentStatus'
            )
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Applications] ADD CONSTRAINT [DF_Applications_CurrentStatus] DEFAULT (''Pending'') FOR [CurrentStatus];';

            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.default_constraints AS defaultInfo
                INNER JOIN sys.columns AS columnInfo
                    ON columnInfo.[object_id] = defaultInfo.[parent_object_id]
                   AND columnInfo.[column_id] = defaultInfo.[parent_column_id]
                WHERE defaultInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[ApplicationStatusHistory]')
                  AND columnInfo.[name] = N'ChangeDate'
            )
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[ApplicationStatusHistory] ADD CONSTRAINT [DF_ApplicationStatusHistory_ChangeDate] DEFAULT (SYSUTCDATETIME()) FOR [ChangeDate];';

            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.default_constraints AS defaultInfo
                INNER JOIN sys.columns AS columnInfo
                    ON columnInfo.[object_id] = defaultInfo.[parent_object_id]
                   AND columnInfo.[column_id] = defaultInfo.[parent_column_id]
                WHERE defaultInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[PasswordResetTokens]')
                  AND columnInfo.[name] = N'IsUsed'
            )
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[PasswordResetTokens] ADD CONSTRAINT [DF_PasswordResetTokens_IsUsed] DEFAULT (0) FOR [IsUsed];';

            DECLARE @restoreDefaultSchema sysname;
            DECLARE @restoreDefaultTable sysname;
            DECLARE @restoreDefaultColumn sysname;
            DECLARE @restoreDefaultConstraint sysname;
            DECLARE @restoreDefaultDefinition nvarchar(max);
            DECLARE @restoreDefaultSql nvarchar(max);
            DECLARE harden_default_restore_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT [SchemaName], [TableName], [ColumnName], [ConstraintName], [Definition]
                FROM #HardenDefaults
                WHERE [HasCanonicalDefault] = 0;

            OPEN harden_default_restore_cursor;
            FETCH NEXT FROM harden_default_restore_cursor
                INTO @restoreDefaultSchema, @restoreDefaultTable, @restoreDefaultColumn, @restoreDefaultConstraint, @restoreDefaultDefinition;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @restoreDefaultSql = N'ALTER TABLE '
                    + QUOTENAME(@restoreDefaultSchema) + N'.' + QUOTENAME(@restoreDefaultTable)
                    + N' ADD CONSTRAINT ' + QUOTENAME(@restoreDefaultConstraint)
                    + N' DEFAULT ' + @restoreDefaultDefinition
                    + N' FOR ' + QUOTENAME(@restoreDefaultColumn) + N';';
                EXEC sys.sp_executesql @restoreDefaultSql;
                FETCH NEXT FROM harden_default_restore_cursor
                    INTO @restoreDefaultSchema, @restoreDefaultTable, @restoreDefaultColumn, @restoreDefaultConstraint, @restoreDefaultDefinition;
            END;
            CLOSE harden_default_restore_cursor;
            DEALLOCATE harden_default_restore_cursor;
            """);

        migrationBuilder.Sql(
            """
            DECLARE @restoreUniqueConstraintId bigint;
            DECLARE @restoreUniqueConstraintSchema sysname;
            DECLARE @restoreUniqueConstraintTable sysname;
            DECLARE @restoreUniqueConstraintName sysname;
            DECLARE @restoreUniqueConstraintIndexType tinyint;
            DECLARE @restoreUniqueConstraintFillFactor tinyint;
            DECLARE @restoreUniqueConstraintIsPadded bit;
            DECLARE @restoreUniqueConstraintIgnoreDuplicateKey bit;
            DECLARE @restoreUniqueConstraintAllowRowLocks bit;
            DECLARE @restoreUniqueConstraintAllowPageLocks bit;
            DECLARE @restoreUniqueConstraintDataSpace sysname;
            DECLARE @restoreUniqueConstraintColumns nvarchar(max);
            DECLARE @restoreUniqueConstraintSql nvarchar(max);

            DECLARE harden_unique_constraint_restore_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT [Id], [SchemaName], [TableName], [ConstraintName], [IndexType], [FillFactor], [IsPadded],
                       [IgnoreDuplicateKey], [AllowRowLocks], [AllowPageLocks], [DataSpaceName]
                FROM #HardenKeyConstraints
                WHERE [ConstraintType] = N'UQ'
                  AND [IsAlterTargetRelated] = 1
                ORDER BY [SchemaName], [TableName], [ConstraintName];

            OPEN harden_unique_constraint_restore_cursor;
            FETCH NEXT FROM harden_unique_constraint_restore_cursor INTO
                @restoreUniqueConstraintId, @restoreUniqueConstraintSchema, @restoreUniqueConstraintTable,
                @restoreUniqueConstraintName, @restoreUniqueConstraintIndexType, @restoreUniqueConstraintFillFactor,
                @restoreUniqueConstraintIsPadded, @restoreUniqueConstraintIgnoreDuplicateKey,
                @restoreUniqueConstraintAllowRowLocks, @restoreUniqueConstraintAllowPageLocks, @restoreUniqueConstraintDataSpace;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @restoreUniqueConstraintColumns = NULL;
                SELECT @restoreUniqueConstraintColumns = STRING_AGG(
                    CAST(QUOTENAME([ColumnName]) + CASE WHEN [IsDescending] = 1 THEN N' DESC' ELSE N' ASC' END AS nvarchar(max)),
                    N', ') WITHIN GROUP (ORDER BY [KeyOrdinal])
                FROM #HardenKeyConstraintColumns
                WHERE [KeyConstraintId] = @restoreUniqueConstraintId;

                IF @restoreUniqueConstraintColumns IS NULL
                BEGIN
                    DECLARE @missingUniqueConstraintColumnsMessage nvarchar(2048) = N'UNIQUE constraint key kolonlari metadata tablosunda bulunamadi: '
                        + QUOTENAME(@restoreUniqueConstraintSchema) + N'.' + QUOTENAME(@restoreUniqueConstraintTable) + N'.'
                        + QUOTENAME(@restoreUniqueConstraintName) + N'. Migration durduruldu.';
                    THROW 51016, @missingUniqueConstraintColumnsMessage, 1;
                END;

                SET @restoreUniqueConstraintSql = N'ALTER TABLE '
                    + QUOTENAME(@restoreUniqueConstraintSchema) + N'.' + QUOTENAME(@restoreUniqueConstraintTable)
                    + N' ADD CONSTRAINT ' + QUOTENAME(@restoreUniqueConstraintName)
                    + N' UNIQUE ' + CASE WHEN @restoreUniqueConstraintIndexType = 1 THEN N'CLUSTERED' ELSE N'NONCLUSTERED' END
                    + N' (' + @restoreUniqueConstraintColumns + N')'
                    + N' WITH (PAD_INDEX = ' + CASE WHEN @restoreUniqueConstraintIsPadded = 1 THEN N'ON' ELSE N'OFF' END
                    + CASE WHEN @restoreUniqueConstraintFillFactor > 0 THEN N', FILLFACTOR = ' + CONVERT(nvarchar(3), @restoreUniqueConstraintFillFactor) ELSE N'' END
                    + N', IGNORE_DUP_KEY = ' + CASE WHEN @restoreUniqueConstraintIgnoreDuplicateKey = 1 THEN N'ON' ELSE N'OFF' END
                    + N', ALLOW_ROW_LOCKS = ' + CASE WHEN @restoreUniqueConstraintAllowRowLocks = 1 THEN N'ON' ELSE N'OFF' END
                    + N', ALLOW_PAGE_LOCKS = ' + CASE WHEN @restoreUniqueConstraintAllowPageLocks = 1 THEN N'ON' ELSE N'OFF' END
                    + N') ON ' + QUOTENAME(@restoreUniqueConstraintDataSpace) + N';';
                EXEC sys.sp_executesql @restoreUniqueConstraintSql;

                FETCH NEXT FROM harden_unique_constraint_restore_cursor INTO
                    @restoreUniqueConstraintId, @restoreUniqueConstraintSchema, @restoreUniqueConstraintTable,
                    @restoreUniqueConstraintName, @restoreUniqueConstraintIndexType, @restoreUniqueConstraintFillFactor,
                    @restoreUniqueConstraintIsPadded, @restoreUniqueConstraintIgnoreDuplicateKey,
                    @restoreUniqueConstraintAllowRowLocks, @restoreUniqueConstraintAllowPageLocks, @restoreUniqueConstraintDataSpace;
            END;
            CLOSE harden_unique_constraint_restore_cursor;
            DEALLOCATE harden_unique_constraint_restore_cursor;
            """);

        migrationBuilder.Sql(
            """
            DECLARE @restoreIndexSchema sysname;
            DECLARE @restoreIndexTable sysname;
            DECLARE @restoreIndexName sysname;
            DECLARE @restoreIndexIsUnique bit;
            DECLARE @restoreIndexIsDisabled bit;
            DECLARE @restoreIndexFillFactor tinyint;
            DECLARE @restoreIndexIsPadded bit;
            DECLARE @restoreIndexIgnoreDuplicateKey bit;
            DECLARE @restoreIndexAllowRowLocks bit;
            DECLARE @restoreIndexAllowPageLocks bit;
            DECLARE @restoreIndexFilterDefinition nvarchar(max);
            DECLARE @restoreIndexDataSpace sysname;
            DECLARE @restoreIndexKeyColumns nvarchar(max);
            DECLARE @restoreIndexIncludedColumns nvarchar(max);
            DECLARE @restoreIndexSql nvarchar(max);
            DECLARE @restoreIndexStateSql nvarchar(max);

            DECLARE harden_index_restore_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT [SchemaName], [TableName], [IndexName], [IsUnique], [IsDisabled], [FillFactor], [IsPadded],
                       [IgnoreDuplicateKey], [AllowRowLocks], [AllowPageLocks], [FilterDefinition], [DataSpaceName]
                FROM #HardenIndexes
                ORDER BY [SchemaName], [TableName], [IndexName];

            OPEN harden_index_restore_cursor;
            FETCH NEXT FROM harden_index_restore_cursor INTO
                @restoreIndexSchema, @restoreIndexTable, @restoreIndexName, @restoreIndexIsUnique, @restoreIndexIsDisabled,
                @restoreIndexFillFactor, @restoreIndexIsPadded, @restoreIndexIgnoreDuplicateKey,
                @restoreIndexAllowRowLocks, @restoreIndexAllowPageLocks, @restoreIndexFilterDefinition, @restoreIndexDataSpace;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @restoreIndexKeyColumns = NULL;
                SET @restoreIndexIncludedColumns = NULL;

                SELECT @restoreIndexKeyColumns = STRING_AGG(
                    CAST(QUOTENAME([ColumnName]) + CASE WHEN [IsDescending] = 1 THEN N' DESC' ELSE N' ASC' END AS nvarchar(max)),
                    N', ') WITHIN GROUP (ORDER BY [KeyOrdinal])
                FROM #HardenIndexColumns
                WHERE [SchemaName] = @restoreIndexSchema
                  AND [TableName] = @restoreIndexTable
                  AND [IndexName] = @restoreIndexName
                  AND [IsIncluded] = 0
                  AND [KeyOrdinal] > 0;

                SELECT @restoreIndexIncludedColumns = STRING_AGG(
                    CAST(QUOTENAME([ColumnName]) AS nvarchar(max)),
                    N', ') WITHIN GROUP (ORDER BY [IndexColumnId])
                FROM #HardenIndexColumns
                WHERE [SchemaName] = @restoreIndexSchema
                  AND [TableName] = @restoreIndexTable
                  AND [IndexName] = @restoreIndexName
                  AND [IsIncluded] = 1;

                IF @restoreIndexKeyColumns IS NULL
                BEGIN
                    DECLARE @missingIndexKeyMessage nvarchar(2048) = N'Yakalanan index icin key kolonu metadata bilgisi bulunamadi: '
                        + QUOTENAME(@restoreIndexSchema) + N'.' + QUOTENAME(@restoreIndexTable) + N'.' + QUOTENAME(@restoreIndexName) + N'. Migration durduruldu.';
                    THROW 51013, @missingIndexKeyMessage, 1;
                END;

                SET @restoreIndexSql = N'CREATE '
                    + CASE WHEN @restoreIndexIsUnique = 1 THEN N'UNIQUE ' ELSE N'' END
                    + N'NONCLUSTERED INDEX ' + QUOTENAME(@restoreIndexName)
                    + N' ON ' + QUOTENAME(@restoreIndexSchema) + N'.' + QUOTENAME(@restoreIndexTable)
                    + N' (' + @restoreIndexKeyColumns + N')'
                    + CASE WHEN @restoreIndexIncludedColumns IS NOT NULL THEN N' INCLUDE (' + @restoreIndexIncludedColumns + N')' ELSE N'' END
                    + CASE WHEN @restoreIndexFilterDefinition IS NOT NULL THEN N' WHERE ' + @restoreIndexFilterDefinition ELSE N'' END
                    + N' WITH (PAD_INDEX = ' + CASE WHEN @restoreIndexIsPadded = 1 THEN N'ON' ELSE N'OFF' END
                    + CASE WHEN @restoreIndexFillFactor > 0 THEN N', FILLFACTOR = ' + CONVERT(nvarchar(3), @restoreIndexFillFactor) ELSE N'' END
                    + N', IGNORE_DUP_KEY = ' + CASE WHEN @restoreIndexIgnoreDuplicateKey = 1 THEN N'ON' ELSE N'OFF' END
                    + N', ALLOW_ROW_LOCKS = ' + CASE WHEN @restoreIndexAllowRowLocks = 1 THEN N'ON' ELSE N'OFF' END
                    + N', ALLOW_PAGE_LOCKS = ' + CASE WHEN @restoreIndexAllowPageLocks = 1 THEN N'ON' ELSE N'OFF' END
                    + N') ON ' + QUOTENAME(@restoreIndexDataSpace) + N';';
                EXEC sys.sp_executesql @restoreIndexSql;

                IF @restoreIndexIsDisabled = 1
                BEGIN
                    SET @restoreIndexStateSql = N'ALTER INDEX ' + QUOTENAME(@restoreIndexName)
                        + N' ON ' + QUOTENAME(@restoreIndexSchema) + N'.' + QUOTENAME(@restoreIndexTable) + N' DISABLE;';
                    EXEC sys.sp_executesql @restoreIndexStateSql;
                END;

                FETCH NEXT FROM harden_index_restore_cursor INTO
                    @restoreIndexSchema, @restoreIndexTable, @restoreIndexName, @restoreIndexIsUnique, @restoreIndexIsDisabled,
                    @restoreIndexFillFactor, @restoreIndexIsPadded, @restoreIndexIgnoreDuplicateKey,
                    @restoreIndexAllowRowLocks, @restoreIndexAllowPageLocks, @restoreIndexFilterDefinition, @restoreIndexDataSpace;
            END;
            CLOSE harden_index_restore_cursor;
            DEALLOCATE harden_index_restore_cursor;
            """);

        migrationBuilder.Sql(
            """
            DECLARE @restoreForeignKeyConstraint sysname;
            DECLARE @restoreForeignKeyParentSchema sysname;
            DECLARE @restoreForeignKeyParentTable sysname;
            DECLARE @restoreForeignKeyParentColumn sysname;
            DECLARE @restoreForeignKeyReferencedSchema sysname;
            DECLARE @restoreForeignKeyReferencedTable sysname;
            DECLARE @restoreForeignKeyReferencedColumn sysname;
            DECLARE @restoreForeignKeyDeleteAction tinyint;
            DECLARE @restoreForeignKeyUpdateAction tinyint;
            DECLARE @restoreForeignKeyNotForReplication bit;
            DECLARE @restoreForeignKeyIsDisabled bit;
            DECLARE @restoreForeignKeyIsNotTrusted bit;
            DECLARE @restoreForeignKeySql nvarchar(max);
            DECLARE @restoreForeignKeyStateSql nvarchar(max);
            DECLARE harden_foreign_key_restore_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT [ConstraintName], [ParentSchema], [ParentTable], [ParentColumn], [ReferencedSchema], [ReferencedTable], [ReferencedColumn],
                       [DeleteAction], [UpdateAction], [IsNotForReplication], [IsDisabled], [IsNotTrusted]
                FROM #HardenForeignKeys;

            OPEN harden_foreign_key_restore_cursor;
            FETCH NEXT FROM harden_foreign_key_restore_cursor INTO
                @restoreForeignKeyConstraint, @restoreForeignKeyParentSchema, @restoreForeignKeyParentTable, @restoreForeignKeyParentColumn,
                @restoreForeignKeyReferencedSchema, @restoreForeignKeyReferencedTable, @restoreForeignKeyReferencedColumn,
                @restoreForeignKeyDeleteAction, @restoreForeignKeyUpdateAction, @restoreForeignKeyNotForReplication,
                @restoreForeignKeyIsDisabled, @restoreForeignKeyIsNotTrusted;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @restoreForeignKeySql = N'ALTER TABLE '
                    + QUOTENAME(@restoreForeignKeyParentSchema) + N'.' + QUOTENAME(@restoreForeignKeyParentTable)
                    + CASE WHEN @restoreForeignKeyIsNotTrusted = 1 OR @restoreForeignKeyIsDisabled = 1 THEN N' WITH NOCHECK' ELSE N' WITH CHECK' END
                    + N' ADD CONSTRAINT ' + QUOTENAME(@restoreForeignKeyConstraint)
                    + N' FOREIGN KEY (' + QUOTENAME(@restoreForeignKeyParentColumn) + N') REFERENCES '
                    + QUOTENAME(@restoreForeignKeyReferencedSchema) + N'.' + QUOTENAME(@restoreForeignKeyReferencedTable)
                    + N' (' + QUOTENAME(@restoreForeignKeyReferencedColumn) + N')'
                    + CASE @restoreForeignKeyDeleteAction WHEN 1 THEN N' ON DELETE CASCADE' WHEN 2 THEN N' ON DELETE SET NULL' WHEN 3 THEN N' ON DELETE SET DEFAULT' ELSE N'' END
                    + CASE @restoreForeignKeyUpdateAction WHEN 1 THEN N' ON UPDATE CASCADE' WHEN 2 THEN N' ON UPDATE SET NULL' WHEN 3 THEN N' ON UPDATE SET DEFAULT' ELSE N'' END
                    + CASE WHEN @restoreForeignKeyNotForReplication = 1 THEN N' NOT FOR REPLICATION' ELSE N'' END
                    + N';';
                EXEC sys.sp_executesql @restoreForeignKeySql;

                IF @restoreForeignKeyIsDisabled = 1
                BEGIN
                    SET @restoreForeignKeyStateSql = N'ALTER TABLE '
                        + QUOTENAME(@restoreForeignKeyParentSchema) + N'.' + QUOTENAME(@restoreForeignKeyParentTable)
                        + N' NOCHECK CONSTRAINT ' + QUOTENAME(@restoreForeignKeyConstraint) + N';';
                    EXEC sys.sp_executesql @restoreForeignKeyStateSql;
                END;

                FETCH NEXT FROM harden_foreign_key_restore_cursor INTO
                    @restoreForeignKeyConstraint, @restoreForeignKeyParentSchema, @restoreForeignKeyParentTable, @restoreForeignKeyParentColumn,
                    @restoreForeignKeyReferencedSchema, @restoreForeignKeyReferencedTable, @restoreForeignKeyReferencedColumn,
                    @restoreForeignKeyDeleteAction, @restoreForeignKeyUpdateAction, @restoreForeignKeyNotForReplication,
                    @restoreForeignKeyIsDisabled, @restoreForeignKeyIsNotTrusted;
            END;
            CLOSE harden_foreign_key_restore_cursor;
            DEALLOCATE harden_foreign_key_restore_cursor;
            """);

        migrationBuilder.Sql(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'[dbo].[Applications]') AND name = N'CK_Applications_CurrentStatus')
                ALTER TABLE [dbo].[Applications] ADD CONSTRAINT [CK_Applications_CurrentStatus] CHECK ([CurrentStatus] IN (N'Pending',N'UnderReview',N'Approved',N'Rejected',N'Withdrawn'));
            """);

        migrationBuilder.Sql(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'[dbo].[PasswordResetTokens]') AND name = N'FK_PasswordResetTokens_Admins_AdminID')
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[PasswordResetTokens] ADD CONSTRAINT [FK_PasswordResetTokens_Admins_AdminID] FOREIGN KEY ([AdminID]) REFERENCES [dbo].[Admins] ([AdminID]);';

            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'[dbo].[PasswordResetTokens]') AND name = N'CK_PasswordResetTokens_Subject')
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[PasswordResetTokens] ADD CONSTRAINT [CK_PasswordResetTokens_Subject] CHECK (([TC] IS NOT NULL AND [AdminID] IS NULL) OR ([TC] IS NULL AND [AdminID] IS NOT NULL));';
            """);

        migrationBuilder.Sql(
            """
            DECLARE @expectedAdminIndexDataSpace sysname =
            (
                SELECT [DataSpaceName]
                FROM #HardenExpectedIndexLocations
                WHERE [SchemaName] = N'dbo' AND [TableName] = N'Admins' AND [IndexName] = N'IX_Admins_NormalizedEmail'
            );
            DECLARE @expectedAdminIndexSql nvarchar(max);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Admins]') AND name = N'IX_Admins_NormalizedEmail')
            BEGIN
                SET @expectedAdminIndexSql = N'CREATE UNIQUE INDEX [IX_Admins_NormalizedEmail] ON [dbo].[Admins] ([NormalizedEmail])'
                    + CASE WHEN @expectedAdminIndexDataSpace IS NOT NULL THEN N' ON ' + QUOTENAME(@expectedAdminIndexDataSpace) ELSE N'' END + N';';
                EXEC sys.sp_executesql @expectedAdminIndexSql;
            END;

            DECLARE @expectedStudentIndexDataSpace sysname =
            (
                SELECT [DataSpaceName]
                FROM #HardenExpectedIndexLocations
                WHERE [SchemaName] = N'dbo' AND [TableName] = N'Students' AND [IndexName] = N'IX_Students_NormalizedEmail'
            );
            DECLARE @expectedStudentIndexSql nvarchar(max);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Students]') AND name = N'IX_Students_NormalizedEmail')
            BEGIN
                SET @expectedStudentIndexSql = N'CREATE UNIQUE INDEX [IX_Students_NormalizedEmail] ON [dbo].[Students] ([NormalizedEmail])'
                    + CASE WHEN @expectedStudentIndexDataSpace IS NOT NULL THEN N' ON ' + QUOTENAME(@expectedStudentIndexDataSpace) ELSE N'' END + N';';
                EXEC sys.sp_executesql @expectedStudentIndexSql;
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Applications]') AND name = N'IX_Applications_TC_ProgramID')
                CREATE UNIQUE INDEX [IX_Applications_TC_ProgramID] ON [dbo].[Applications] ([TC], [ProgramID]);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[PasswordResetTokens]') AND name = N'IX_PasswordResetTokens_AdminID')
                EXEC sys.sp_executesql N'CREATE INDEX [IX_PasswordResetTokens_AdminID] ON [dbo].[PasswordResetTokens] ([AdminID]);';
            """);

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'[dbo].[SecurityAuditLogs]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[SecurityAuditLogs]
                (
                    [AuditId] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SecurityAuditLogs] PRIMARY KEY,
                    [ActorAdminID] int NULL,
                    [EventType] nvarchar(100) NOT NULL,
                    [TargetType] nvarchar(100) NOT NULL,
                    [TargetId] varchar(100) NOT NULL,
                    [Details] nvarchar(1000) NULL,
                    [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_SecurityAuditLogs_CreatedAtUtc] DEFAULT (SYSUTCDATETIME())
                );
            END;
            """);

        // SQL Server must compile this after the table-creation command has completed.
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'[dbo].[SecurityAuditLogs]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[SecurityAuditLogs]') AND name = N'IX_SecurityAuditLogs_CreatedAtUtc')
                EXEC sys.sp_executesql N'CREATE INDEX [IX_SecurityAuditLogs_CreatedAtUtc] ON [dbo].[SecurityAuditLogs] ([CreatedAtUtc]);';
            """);

        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS #HardenIndexColumns;
            DROP TABLE IF EXISTS #HardenIndexes;
            DROP TABLE IF EXISTS #HardenKeyConstraintColumns;
            DROP TABLE IF EXISTS #HardenKeyConstraints;
            DROP TABLE IF EXISTS #HardenExpectedIndexLocations;
            DROP TABLE IF EXISTS #HardenExpectedIndexColumns;
            DROP TABLE IF EXISTS #HardenExpectedIndexes;
            DROP TABLE IF EXISTS #HardenForeignKeys;
            DROP TABLE IF EXISTS #HardenDefaults;
            DROP TABLE IF EXISTS #HardenAlterTargets;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "Bu güvenlik migration'ı veri kaybı riski nedeniyle otomatik olarak geri alınamaz. Geri dönüş için doğrulanmış yedek ve manuel DBA planı gerekir.");
    }
}
