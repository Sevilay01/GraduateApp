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

            CREATE TABLE #HardenAlterTargets
            (
                [TableName] sysname NOT NULL,
                [ColumnName] sysname NOT NULL,
                [HasCanonicalDefault] bit NOT NULL,
                CONSTRAINT [PK_HardenAlterTargets] PRIMARY KEY ([TableName], [ColumnName])
            );

            INSERT INTO #HardenAlterTargets ([TableName], [ColumnName], [HasCanonicalDefault])
            VALUES
                (N'Admins', N'NormalizedEmail', 0),
                (N'Admins', N'Email', 0),
                (N'Admins', N'PasswordHash', 0),
                (N'Students', N'NormalizedEmail', 0),
                (N'Students', N'Email', 0),
                (N'Students', N'PasswordHash', 0),
                (N'Applications', N'ApplicationDate', 1),
                (N'Applications', N'CurrentStatus', 1),
                (N'ApplicationStatusHistory', N'ChangeDate', 1),
                (N'ApplicationStatusHistory', N'Notes', 0),
                (N'PasswordResetTokens', N'TC', 0),
                (N'PasswordResetTokens', N'IsUsed', 1),
                (N'PasswordResetTokens', N'ExpirationDate', 0);

            CREATE TABLE #HardenDefaults
            (
                [SchemaName] sysname NOT NULL,
                [TableName] sysname NOT NULL,
                [ColumnName] sysname NOT NULL,
                [ConstraintName] sysname NOT NULL,
                [Definition] nvarchar(max) NOT NULL,
                [HasCanonicalDefault] bit NOT NULL,
                CONSTRAINT [PK_HardenDefaults] PRIMARY KEY ([SchemaName], [ConstraintName])
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
            WHERE schemaInfo.[name] = N'dbo';

            CREATE TABLE #HardenForeignKeys
            (
                [ConstraintName] sysname NOT NULL PRIMARY KEY,
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

            IF EXISTS
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
              AND parentColumn.[name] = N'TC';

            IF EXISTS
            (
                SELECT 1
                FROM sys.indexes AS indexInfo
                INNER JOIN sys.index_columns AS indexColumnInfo
                    ON indexColumnInfo.[object_id] = indexInfo.[object_id]
                   AND indexColumnInfo.[index_id] = indexInfo.[index_id]
                INNER JOIN sys.columns AS indexedColumn
                    ON indexedColumn.[object_id] = indexColumnInfo.[object_id]
                   AND indexedColumn.[column_id] = indexColumnInfo.[column_id]
                INNER JOIN sys.tables AS indexedTable
                    ON indexedTable.[object_id] = indexInfo.[object_id]
                INNER JOIN sys.schemas AS indexedSchema
                    ON indexedSchema.[schema_id] = indexedTable.[schema_id]
                INNER JOIN #HardenAlterTargets AS target
                    ON target.[TableName] = indexedTable.[name]
                   AND target.[ColumnName] = indexedColumn.[name]
                WHERE indexedSchema.[name] = N'dbo'
                  AND indexInfo.[index_id] > 0
                  AND indexInfo.[is_hypothetical] = 0
                  AND NOT
                  (
                      indexInfo.[is_primary_key] = 0
                      AND indexInfo.[is_unique_constraint] = 0
                      AND
                      (
                          (indexedTable.[name] = N'Admins' AND indexInfo.[name] = N'IX_Admins_NormalizedEmail')
                          OR (indexedTable.[name] = N'Students' AND indexInfo.[name] = N'IX_Students_NormalizedEmail')
                      )
                  )
            )
                THROW 51007, 'ALTER COLUMN hedeflerinden birinde güvenle yeniden üretilemeyen index bağımlılığı bulundu. Migration durduruldu.', 1;

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
                    EXISTS (SELECT 1 FROM #HardenAlterTargets AS parentTarget WHERE parentTarget.[TableName] = parentTable.[name] AND parentTarget.[ColumnName] = parentColumn.[name])
                    OR EXISTS (SELECT 1 FROM #HardenAlterTargets AS referencedTarget WHERE referencedTarget.[TableName] = referencedTable.[name] AND referencedTarget.[ColumnName] = referencedColumn.[name])
                )
                  AND NOT (foreignKeyColumn.[parent_object_id] = OBJECT_ID(N'[dbo].[PasswordResetTokens]') AND parentColumn.[name] = N'TC')
            )
                THROW 51009, 'ALTER COLUMN hedeflerinden birinde güvenle yeniden üretilemeyen foreign key bağımlılığı bulundu. Migration durduruldu.', 1;
            """);

        migrationBuilder.Sql(
            """
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

            IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Admins]') AND [name] = N'IX_Admins_NormalizedEmail' AND [is_primary_key] = 0 AND [is_unique_constraint] = 0)
                EXEC sys.sp_executesql N'DROP INDEX [IX_Admins_NormalizedEmail] ON [dbo].[Admins];';

            IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[Students]') AND [name] = N'IX_Students_NormalizedEmail' AND [is_primary_key] = 0 AND [is_unique_constraint] = 0)
                EXEC sys.sp_executesql N'DROP INDEX [IX_Students_NormalizedEmail] ON [dbo].[Students];';

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
            """);

        migrationBuilder.Sql(
            """
            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Admins] ALTER COLUMN [NormalizedEmail] nvarchar(254) NOT NULL;';
            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Admins] ALTER COLUMN [Email] nvarchar(254) NOT NULL;';
            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Admins] ALTER COLUMN [PasswordHash] varchar(512) NOT NULL;';

            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Students] ALTER COLUMN [NormalizedEmail] nvarchar(254) NOT NULL;';
            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Students] ALTER COLUMN [Email] nvarchar(254) NOT NULL;';
            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Students] ALTER COLUMN [PasswordHash] varchar(512) NOT NULL;';

            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Applications] ALTER COLUMN [ApplicationDate] datetime2 NOT NULL;';
            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Applications] ALTER COLUMN [CurrentStatus] nvarchar(50) NOT NULL;';

            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[ApplicationStatusHistory] ALTER COLUMN [ChangeDate] datetime2 NOT NULL;';
            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[ApplicationStatusHistory] ALTER COLUMN [Notes] nvarchar(500) NULL;';

            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[PasswordResetTokens] ALTER COLUMN [TC] char(11) NULL;';
            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[PasswordResetTokens] ALTER COLUMN [IsUsed] bit NOT NULL;';
            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[PasswordResetTokens] ALTER COLUMN [ExpirationDate] datetime2 NOT NULL;';
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
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Admins]') AND name = N'IX_Admins_NormalizedEmail')
                EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_Admins_NormalizedEmail] ON [dbo].[Admins] ([NormalizedEmail]);';

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Students]') AND name = N'IX_Students_NormalizedEmail')
                EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_Students_NormalizedEmail] ON [dbo].[Students] ([NormalizedEmail]);';

            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'[dbo].[Applications]') AND name = N'CK_Applications_CurrentStatus')
                ALTER TABLE [dbo].[Applications] ADD CONSTRAINT [CK_Applications_CurrentStatus] CHECK ([CurrentStatus] IN (N'Pending',N'UnderReview',N'Approved',N'Rejected',N'Withdrawn'));

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Applications]') AND name = N'IX_Applications_TC_ProgramID')
                CREATE UNIQUE INDEX [IX_Applications_TC_ProgramID] ON [dbo].[Applications] ([TC], [ProgramID]);
            """);

        migrationBuilder.Sql(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'[dbo].[PasswordResetTokens]') AND name = N'FK_PasswordResetTokens_Admins_AdminID')
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[PasswordResetTokens] ADD CONSTRAINT [FK_PasswordResetTokens_Admins_AdminID] FOREIGN KEY ([AdminID]) REFERENCES [dbo].[Admins] ([AdminID]);';

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[PasswordResetTokens]') AND name = N'IX_PasswordResetTokens_AdminID')
                EXEC sys.sp_executesql N'CREATE INDEX [IX_PasswordResetTokens_AdminID] ON [dbo].[PasswordResetTokens] ([AdminID]);';

            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'[dbo].[PasswordResetTokens]') AND name = N'CK_PasswordResetTokens_Subject')
                EXEC sys.sp_executesql N'ALTER TABLE [dbo].[PasswordResetTokens] ADD CONSTRAINT [CK_PasswordResetTokens_Subject] CHECK (([TC] IS NOT NULL AND [AdminID] IS NULL) OR ([TC] IS NULL AND [AdminID] IS NOT NULL));';
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
