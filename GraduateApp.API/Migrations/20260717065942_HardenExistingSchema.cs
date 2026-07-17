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
                  AND [CurrentStatus] NOT IN ('Pending','UnderReview','Approved','Rejected','Withdrawn','Sisteme Alındı','Onay Bekliyor','İnceleniyor','Onaylandı','Reddedildi'))
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
            SET [CurrentStatus] = 'Pending'
            WHERE [CurrentStatus] IS NULL;

            UPDATE [dbo].[Applications]
            SET [CurrentStatus] = CASE [CurrentStatus]
                WHEN 'Sisteme Alındı' THEN 'Pending'
                WHEN 'Onay Bekliyor' THEN 'Pending'
                WHEN 'İnceleniyor' THEN 'UnderReview'
                WHEN 'Onaylandı' THEN 'Approved'
                WHEN 'Reddedildi' THEN 'Rejected'
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

        migrationBuilder.Sql(
            """
            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Admins] ALTER COLUMN [NormalizedEmail] nvarchar(254) NOT NULL;';
            ALTER TABLE [dbo].[Admins] ALTER COLUMN [Email] nvarchar(254) NOT NULL;
            ALTER TABLE [dbo].[Admins] ALTER COLUMN [PasswordHash] varchar(512) NOT NULL;

            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Students] ALTER COLUMN [NormalizedEmail] nvarchar(254) NOT NULL;';
            ALTER TABLE [dbo].[Students] ALTER COLUMN [Email] nvarchar(254) NOT NULL;
            ALTER TABLE [dbo].[Students] ALTER COLUMN [PasswordHash] varchar(512) NOT NULL;

            ALTER TABLE [dbo].[Applications] ALTER COLUMN [ApplicationDate] datetime2 NOT NULL;
            ALTER TABLE [dbo].[Applications] ALTER COLUMN [CurrentStatus] nvarchar(50) NOT NULL;

            ALTER TABLE [dbo].[ApplicationStatusHistory] ALTER COLUMN [ChangeDate] datetime2 NOT NULL;
            ALTER TABLE [dbo].[ApplicationStatusHistory] ALTER COLUMN [Notes] nvarchar(500) NULL;

            ALTER TABLE [dbo].[PasswordResetTokens] ALTER COLUMN [TC] char(11) NULL;
            ALTER TABLE [dbo].[PasswordResetTokens] ALTER COLUMN [IsUsed] bit NOT NULL;
            ALTER TABLE [dbo].[PasswordResetTokens] ALTER COLUMN [ExpirationDate] datetime2 NOT NULL;
            """);

        migrationBuilder.Sql(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Admins]') AND name = N'IX_Admins_NormalizedEmail')
                EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_Admins_NormalizedEmail] ON [dbo].[Admins] ([NormalizedEmail]);';

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Students]') AND name = N'IX_Students_NormalizedEmail')
                EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_Students_NormalizedEmail] ON [dbo].[Students] ([NormalizedEmail]);';

            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'[dbo].[Applications]') AND name = N'CK_Applications_CurrentStatus')
                ALTER TABLE [dbo].[Applications] ADD CONSTRAINT [CK_Applications_CurrentStatus] CHECK ([CurrentStatus] IN ('Pending','UnderReview','Approved','Rejected','Withdrawn'));

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[Applications]') AND name = N'IX_Applications_TC_ProgramID')
                CREATE UNIQUE INDEX [IX_Applications_TC_ProgramID] ON [dbo].[Applications] ([TC], [ProgramID]);

            DECLARE @applicationStatusDefault sysname;
            SELECT @applicationStatusDefault = dc.name
            FROM sys.default_constraints dc
            JOIN sys.columns c ON c.default_object_id = dc.object_id
            WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[Applications]') AND c.name = N'CurrentStatus';
            IF @applicationStatusDefault IS NOT NULL
                EXEC(N'ALTER TABLE [dbo].[Applications] DROP CONSTRAINT ' + QUOTENAME(@applicationStatusDefault));
            ALTER TABLE [dbo].[Applications] ADD CONSTRAINT [DF_Applications_CurrentStatus] DEFAULT ('Pending') FOR [CurrentStatus];
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
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "Bu güvenlik migration'ı veri kaybı riski nedeniyle otomatik olarak geri alınamaz. Geri dönüş için doğrulanmış yedek ve manuel DBA planı gerekir.");
    }
}
