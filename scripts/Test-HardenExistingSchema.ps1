[CmdletBinding()]
param(
    [string]$SqlServer = '(localdb)\MSSQLLocalDB'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$databasePrefix = 'GraduateAppMigrationTest_'
$createdDatabases = [System.Collections.Generic.List[string]]::new()
$previousConnectionString = [Environment]::GetEnvironmentVariable('ConnectionStrings__DefaultConnection', 'Process')

function Quote-SqlIdentifier {
    param([Parameter(Mandatory)][string]$Value)

    return '[' + $Value.Replace(']', ']]') + ']'
}

function Invoke-Sql {
    param(
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$Query
    )

    $output = & sqlcmd -S $SqlServer -E -b -l 30 -d $Database -Q $Query 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed for temporary database '$Database': $($output -join [Environment]::NewLine)"
    }
}

function New-TestDatabase {
    param([Parameter(Mandatory)][string]$Scenario)

    $databaseName = $databasePrefix + $Scenario + '_' + [Guid]::NewGuid().ToString('N')
    if (-not [Regex]::IsMatch(
        $databaseName,
        '^GraduateAppMigrationTest_[A-Za-z0-9_]+$',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        throw 'Unsafe temporary database name generated.'
    }

    Invoke-Sql -Database 'master' -Query "CREATE DATABASE $(Quote-SqlIdentifier $databaseName);"
    [void]$createdDatabases.Add($databaseName)
    return $databaseName
}

function Initialize-LegacySchema {
    param(
        [Parameter(Mandatory)][string]$Database,
        [string]$SeedSql = ''
    )

    $legacySchema = @"
CREATE TABLE [dbo].[Admins]
(
    [AdminID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Admins] PRIMARY KEY,
    [Email] nvarchar(320) NOT NULL,
    [PasswordHash] varchar(255) NOT NULL
);

CREATE TABLE [dbo].[Students]
(
    [TC] char(11) NOT NULL CONSTRAINT [PK_Students] PRIMARY KEY,
    [Email] nvarchar(320) NOT NULL,
    [PasswordHash] varchar(255) NOT NULL
);

CREATE TABLE [dbo].[Programs]
(
    [ProgramID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Programs] PRIMARY KEY,
    [ProgramName] nvarchar(100) NOT NULL
);

CREATE TABLE [dbo].[Applications]
(
    [ApplicationID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Applications] PRIMARY KEY,
    [TC] char(11) NOT NULL,
    [ProgramID] int NOT NULL,
    [ApplicationDate] datetime NULL DEFAULT (GETDATE()),
    [CurrentStatus] nvarchar(50) NULL DEFAULT (N'Sisteme Alındı'),
    FOREIGN KEY ([TC]) REFERENCES [dbo].[Students] ([TC]),
    FOREIGN KEY ([ProgramID]) REFERENCES [dbo].[Programs] ([ProgramID]),
    CHECK ([CurrentStatus] IS NULL OR [CurrentStatus] IN (N'Pending', N'UnderReview', N'Approved', N'Rejected', N'Withdrawn', N'Sisteme Alındı', N'Onay Bekliyor', N'İnceleniyor', N'Onaylandı', N'Reddedildi'))
);

CREATE TABLE [dbo].[ApplicationStatusHistory]
(
    [HistoryID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ApplicationStatusHistory] PRIMARY KEY,
    [ApplicationID] int NULL,
    [StatusName] nvarchar(50) NOT NULL,
    [ChangeDate] datetime NULL DEFAULT (GETDATE()),
    [Notes] nvarchar(max) NULL
);

CREATE TABLE [dbo].[PasswordResetTokens]
(
    [TokenID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PasswordResetTokens] PRIMARY KEY,
    [TC] char(11) NOT NULL,
    [TokenHash] varchar(256) NOT NULL,
    [ExpirationDate] datetime NOT NULL DEFAULT (DATEADD(hour, 1, GETDATE())),
    [IsUsed] bit NULL DEFAULT (0),
    FOREIGN KEY ([TC]) REFERENCES [dbo].[Students] ([TC]) ON DELETE CASCADE
);

CREATE TABLE [dbo].[MigrationTestExpected]
(
    [PropertyName] sysname NOT NULL CONSTRAINT [PK_MigrationTestExpected] PRIMARY KEY,
    [PropertyValue] nvarchar(max) NOT NULL
);

INSERT INTO [dbo].[MigrationTestExpected] ([PropertyName], [PropertyValue])
SELECT N'ExpirationDateDefaultName', defaultInfo.[name]
FROM sys.default_constraints AS defaultInfo
INNER JOIN sys.columns AS columnInfo
    ON columnInfo.[object_id] = defaultInfo.[parent_object_id]
   AND columnInfo.[column_id] = defaultInfo.[parent_column_id]
WHERE defaultInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[PasswordResetTokens]')
  AND columnInfo.[name] = N'ExpirationDate';

$SeedSql
"@

    Invoke-Sql -Database $Database -Query $legacySchema
}

function Invoke-EfDatabaseUpdate {
    param(
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][bool]$ExpectSuccess,
        [string]$ExpectedErrorContains = ''
    )

    [Environment]::SetEnvironmentVariable(
        'ConnectionStrings__DefaultConnection',
        "Server=$SqlServer;Database=$Database;Integrated Security=true;TrustServerCertificate=true",
        'Process')

    Push-Location $repositoryRoot
    try {
        $output = & dotnet ef database update --project GraduateApp.API --startup-project GraduateApp.API --configuration Release --no-build 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($ExpectSuccess -and $exitCode -ne 0) {
        throw "EF migration unexpectedly failed for '$Database': $($output -join [Environment]::NewLine)"
    }

    if (-not $ExpectSuccess -and $exitCode -eq 0) {
        throw "EF migration unexpectedly succeeded for negative scenario '$Database'."
    }

    if (-not $ExpectSuccess -and $ExpectedErrorContains -and
        (($output -join [Environment]::NewLine).IndexOf($ExpectedErrorContains, [StringComparison]::Ordinal) -lt 0)) {
        throw "EF migration failed for '$Database', but the expected safe-stop identifier was not reported."
    }
}

function Assert-SuccessScenario {
    param([Parameter(Mandatory)][string]$Database)

    $assertions = @"
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260717065942_HardenExistingSchema')
    THROW 52000, 'Migration history row is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints AS defaultInfo
    INNER JOIN sys.columns AS columnInfo
        ON columnInfo.[object_id] = defaultInfo.[parent_object_id]
       AND columnInfo.[column_id] = defaultInfo.[parent_column_id]
    WHERE defaultInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[Applications]')
      AND columnInfo.[name] = N'ApplicationDate'
      AND defaultInfo.[name] = N'DF_Applications_ApplicationDate'
      AND defaultInfo.[definition] LIKE N'%SYSUTCDATETIME%'
)
    THROW 52001, 'Canonical ApplicationDate default is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints AS defaultInfo
    INNER JOIN sys.columns AS columnInfo
        ON columnInfo.[object_id] = defaultInfo.[parent_object_id]
       AND columnInfo.[column_id] = defaultInfo.[parent_column_id]
    WHERE defaultInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[Applications]')
      AND columnInfo.[name] = N'CurrentStatus'
      AND defaultInfo.[name] = N'DF_Applications_CurrentStatus'
      AND defaultInfo.[definition] LIKE N'%Pending%'
)
    THROW 52002, 'Canonical CurrentStatus default is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints AS defaultInfo
    INNER JOIN sys.columns AS columnInfo
        ON columnInfo.[object_id] = defaultInfo.[parent_object_id]
       AND columnInfo.[column_id] = defaultInfo.[parent_column_id]
    WHERE defaultInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[ApplicationStatusHistory]')
      AND columnInfo.[name] = N'ChangeDate'
      AND defaultInfo.[name] = N'DF_ApplicationStatusHistory_ChangeDate'
      AND defaultInfo.[definition] LIKE N'%SYSUTCDATETIME%'
)
    THROW 52003, 'Canonical ChangeDate default is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints AS defaultInfo
    INNER JOIN sys.columns AS columnInfo
        ON columnInfo.[object_id] = defaultInfo.[parent_object_id]
       AND columnInfo.[column_id] = defaultInfo.[parent_column_id]
    WHERE defaultInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[PasswordResetTokens]')
      AND columnInfo.[name] = N'IsUsed'
      AND defaultInfo.[name] = N'DF_PasswordResetTokens_IsUsed'
      AND defaultInfo.[definition] IN (N'((0))', N'(0)')
)
    THROW 52004, 'Canonical IsUsed default is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints AS defaultInfo
    INNER JOIN sys.columns AS columnInfo
        ON columnInfo.[object_id] = defaultInfo.[parent_object_id]
       AND columnInfo.[column_id] = defaultInfo.[parent_column_id]
    WHERE defaultInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[PasswordResetTokens]')
      AND columnInfo.[name] = N'ExpirationDate'
      AND defaultInfo.[name] = (SELECT [PropertyValue] FROM [dbo].[MigrationTestExpected] WHERE [PropertyName] = N'ExpirationDateDefaultName')
      AND defaultInfo.[definition] LIKE N'%dateadd%'
)
    THROW 52005, 'Non-canonical ExpirationDate default was not preserved.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys AS foreignKeyInfo
    INNER JOIN sys.foreign_key_columns AS foreignKeyColumn
        ON foreignKeyColumn.[constraint_object_id] = foreignKeyInfo.[object_id]
    INNER JOIN sys.columns AS parentColumn
        ON parentColumn.[object_id] = foreignKeyColumn.[parent_object_id]
       AND parentColumn.[column_id] = foreignKeyColumn.[parent_column_id]
    WHERE foreignKeyColumn.[parent_object_id] = OBJECT_ID(N'[dbo].[PasswordResetTokens]')
      AND parentColumn.[name] = N'TC'
      AND foreignKeyInfo.[delete_referential_action] = 1
)
    THROW 52006, 'Legacy PasswordResetTokens.TC foreign key was not preserved.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[Applications]') AND [name] = N'CK_Applications_CurrentStatus')
    THROW 52007, 'Canonical CurrentStatus check constraint is missing.', 1;

IF EXISTS (SELECT 1 FROM [dbo].[Applications] WHERE [CurrentStatus] <> N'Pending' OR [ApplicationDate] IS NULL)
    THROW 52008, 'Legacy application data was not normalized.', 1;

IF EXISTS (SELECT 1 FROM [dbo].[ApplicationStatusHistory] WHERE [ChangeDate] IS NULL)
    THROW 52009, 'Legacy status history data was not backfilled.', 1;

IF EXISTS (SELECT 1 FROM [dbo].[PasswordResetTokens] WHERE [TokenHash] = 'legacy-token' AND [IsUsed] <> 1)
    THROW 52010, 'Legacy password reset token was not invalidated.', 1;

INSERT INTO [dbo].[Programs] ([ProgramName]) VALUES (N'Program 2');
INSERT INTO [dbo].[Applications] ([TC], [ProgramID]) VALUES ('00000000000', 2);
IF NOT EXISTS (SELECT 1 FROM [dbo].[Applications] WHERE [ProgramID] = 2 AND [CurrentStatus] = N'Pending' AND [ApplicationDate] IS NOT NULL)
    THROW 52011, 'Canonical application defaults do not work for new rows.', 1;

INSERT INTO [dbo].[ApplicationStatusHistory] ([ApplicationID], [StatusName]) VALUES (2, N'Pending');
IF NOT EXISTS (SELECT 1 FROM [dbo].[ApplicationStatusHistory] WHERE [ApplicationID] = 2 AND [ChangeDate] IS NOT NULL)
    THROW 52012, 'Canonical ChangeDate default does not work for new rows.', 1;

INSERT INTO [dbo].[PasswordResetTokens] ([TC], [TokenHash], [ExpirationDate])
VALUES ('00000000000', 'new-token', DATEADD(hour, 1, SYSUTCDATETIME()));
IF NOT EXISTS (SELECT 1 FROM [dbo].[PasswordResetTokens] WHERE [TokenHash] = 'new-token' AND [IsUsed] = 0)
    THROW 52013, 'Canonical IsUsed default does not work for new rows.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Admins]')
      AND [name] = N'IX_Legacy_Admins_Email'
      AND [type] = 2
      AND [is_unique] = 0
      AND [is_disabled] = 0
      AND [fill_factor] = 80
      AND [is_padded] = 1
      AND [ignore_dup_key] = 0
      AND [allow_row_locks] = 0
      AND [allow_page_locks] = 1
)
    THROW 52014, 'Single-column index options were not preserved.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Students]')
      AND [name] = N'UX_Legacy_Students_Email'
      AND [type] = 2
      AND [is_unique] = 1
      AND [ignore_dup_key] = 1
      AND [fill_factor] = 75
)
    THROW 52015, 'Unique index properties were not preserved.', 1;

DECLARE @applicationCompositeIndexId int =
(
    SELECT [index_id]
    FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Applications]')
      AND [name] = N'IX_Legacy_Applications_StatusDate'
      AND [type] = 2
      AND [is_unique] = 0
      AND [has_filter] = 1
      AND [filter_definition] LIKE N'%ApplicationDate%'
      AND [fill_factor] = 70
      AND [is_padded] = 1
      AND [allow_row_locks] = 1
      AND [allow_page_locks] = 0
);

IF @applicationCompositeIndexId IS NULL
    THROW 52016, 'Filtered composite index properties were not preserved.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.index_columns AS indexColumnInfo
    INNER JOIN sys.columns AS columnInfo
        ON columnInfo.[object_id] = indexColumnInfo.[object_id]
       AND columnInfo.[column_id] = indexColumnInfo.[column_id]
    WHERE indexColumnInfo.[object_id] = OBJECT_ID(N'[dbo].[Applications]')
      AND indexColumnInfo.[index_id] = @applicationCompositeIndexId
      AND columnInfo.[name] = N'CurrentStatus'
      AND indexColumnInfo.[key_ordinal] = 1
      AND indexColumnInfo.[is_descending_key] = 1
      AND indexColumnInfo.[is_included_column] = 0
)
OR NOT EXISTS
(
    SELECT 1
    FROM sys.index_columns AS indexColumnInfo
    INNER JOIN sys.columns AS columnInfo
        ON columnInfo.[object_id] = indexColumnInfo.[object_id]
       AND columnInfo.[column_id] = indexColumnInfo.[column_id]
    WHERE indexColumnInfo.[object_id] = OBJECT_ID(N'[dbo].[Applications]')
      AND indexColumnInfo.[index_id] = @applicationCompositeIndexId
      AND columnInfo.[name] = N'ApplicationDate'
      AND indexColumnInfo.[key_ordinal] = 2
      AND indexColumnInfo.[is_descending_key] = 0
      AND indexColumnInfo.[is_included_column] = 0
)
    THROW 52017, 'Composite index key order or ASC/DESC direction was not preserved.', 1;

IF (SELECT COUNT_BIG(*)
    FROM sys.index_columns AS indexColumnInfo
    INNER JOIN sys.columns AS columnInfo
        ON columnInfo.[object_id] = indexColumnInfo.[object_id]
       AND columnInfo.[column_id] = indexColumnInfo.[column_id]
    WHERE indexColumnInfo.[object_id] = OBJECT_ID(N'[dbo].[Applications]')
      AND indexColumnInfo.[index_id] = @applicationCompositeIndexId
      AND indexColumnInfo.[is_included_column] = 1
      AND columnInfo.[name] IN (N'TC', N'ProgramID')) <> 2
    THROW 52018, 'Composite index INCLUDE columns were not preserved.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[ApplicationStatusHistory]')
      AND [name] = N'IX_Legacy_History_ChangeDate_Disabled'
      AND [is_disabled] = 1
)
    THROW 52019, 'Disabled index state was not restored.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexInfo
    INNER JOIN sys.data_spaces AS dataSpaceInfo
        ON dataSpaceInfo.[data_space_id] = indexInfo.[data_space_id]
    WHERE indexInfo.[object_id] = OBJECT_ID(N'[dbo].[Applications]')
      AND indexInfo.[name] = N'IX_Legacy_Applications_StatusDate'
      AND dataSpaceInfo.[name] = N'PRIMARY'
      AND dataSpaceInfo.[type] = N'FG'
)
    THROW 52020, 'Index filegroup/data space was not preserved.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[PasswordResetTokens]')
      AND [name] = N'IX_Legacy_PasswordResetTokens_TC_AlreadyTarget'
      AND [fill_factor] = 77
)
OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE [class] = 7
      AND [major_id] = OBJECT_ID(N'[dbo].[PasswordResetTokens]')
      AND [name] = N'MigrationTestMarker'
      AND CONVERT(nvarchar(100), [value]) = N'index-was-not-recreated'
)
    THROW 52021, 'Index on an already-target-typed column was unnecessarily recreated.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexInfo
    INNER JOIN sys.extended_properties AS propertyInfo
        ON propertyInfo.[class] = 7
       AND propertyInfo.[major_id] = indexInfo.[object_id]
       AND propertyInfo.[minor_id] = indexInfo.[index_id]
    WHERE indexInfo.[object_id] = OBJECT_ID(N'[dbo].[Admins]')
      AND indexInfo.[name] = N'IX_Admins_NormalizedEmail'
      AND indexInfo.[is_unique] = 1
      AND propertyInfo.[name] = N'MigrationExpectedIndexMarker'
      AND CONVERT(nvarchar(100), propertyInfo.[value]) = N'validated-and-not-recreated'
)
    THROW 52022, 'A compatible migration-owned index was not accepted and preserved.', 1;
"@

    Invoke-Sql -Database $Database -Query $assertions
}

function Assert-PreflightRollback {
    param([Parameter(Mandatory)][string]$Database)

    $assertions = @"
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260717065942_HardenExistingSchema')
    THROW 52100, 'Rejected migration was recorded in migration history.', 1;

IF COL_LENGTH(N'dbo.Admins', N'NormalizedEmail') IS NOT NULL
    THROW 52101, 'Rejected migration changed the legacy schema.', 1;
"@

    Invoke-Sql -Database $Database -Query $assertions
}

function Assert-RejectedMigrationNotRecorded {
    param([Parameter(Mandatory)][string]$Database)

    $assertions = @"
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260717065942_HardenExistingSchema')
    THROW 52102, 'Rejected migration was recorded in migration history.', 1;
"@

    Invoke-Sql -Database $Database -Query $assertions
}

function Assert-KeyConstraintPreserved {
    param(
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][ValidateSet('PK', 'UQ')][string]$ConstraintType,
        [Parameter(Mandatory)][string]$ConstraintName
    )

    $constraintNameLiteral = $ConstraintName.Replace("'", "''")
    $assertions = @"
IF NOT EXISTS
(
    SELECT 1
    FROM sys.key_constraints
    WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[Admins]')
      AND [name] = N'$constraintNameLiteral'
      AND [type] = N'$ConstraintType'
)
    THROW 52103, 'Rejected migration did not preserve the blocking key constraint.', 1;
"@

    Invoke-Sql -Database $Database -Query $assertions
}

try {
    $successDatabase = New-TestDatabase -Scenario 'Success'
    Initialize-LegacySchema -Database $successDatabase -SeedSql @"
INSERT INTO [dbo].[Admins] ([Email], [PasswordHash]) VALUES (N'admin@example.test', 'legacy-hash');
INSERT INTO [dbo].[Students] ([TC], [Email], [PasswordHash]) VALUES ('00000000000', N'student@example.test', 'legacy-hash');
INSERT INTO [dbo].[Programs] ([ProgramName]) VALUES (N'Program 1');
INSERT INTO [dbo].[Applications] ([TC], [ProgramID], [ApplicationDate], [CurrentStatus]) VALUES ('00000000000', 1, NULL, N'Pending');
INSERT INTO [dbo].[ApplicationStatusHistory] ([ApplicationID], [StatusName], [ChangeDate], [Notes]) VALUES (1, N'Pending', NULL, N'legacy');
INSERT INTO [dbo].[PasswordResetTokens] ([TC], [TokenHash], [ExpirationDate], [IsUsed]) VALUES ('00000000000', 'legacy-token', DATEADD(hour, 1, GETDATE()), NULL);

ALTER TABLE [dbo].[Admins] ADD [NormalizedEmail] nvarchar(254) NULL;
UPDATE [dbo].[Admins] SET [NormalizedEmail] = UPPER([Email]);
ALTER TABLE [dbo].[Admins] ALTER COLUMN [NormalizedEmail] nvarchar(254) NOT NULL;
CREATE UNIQUE INDEX [IX_Admins_NormalizedEmail] ON [dbo].[Admins] ([NormalizedEmail]);
EXEC sys.sp_addextendedproperty
    @name = N'MigrationExpectedIndexMarker',
    @value = N'validated-and-not-recreated',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Admins',
    @level2type = N'INDEX', @level2name = N'IX_Admins_NormalizedEmail';

CREATE INDEX [IX_Legacy_Admins_Email]
ON [dbo].[Admins] ([Email] ASC)
WITH (PAD_INDEX = ON, FILLFACTOR = 80, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = OFF, ALLOW_PAGE_LOCKS = ON);

CREATE UNIQUE INDEX [UX_Legacy_Students_Email]
ON [dbo].[Students] ([Email] DESC)
WITH (PAD_INDEX = OFF, FILLFACTOR = 75, IGNORE_DUP_KEY = ON, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON);

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
CREATE INDEX [IX_Legacy_Applications_StatusDate]
ON [dbo].[Applications] ([CurrentStatus] DESC, [ApplicationDate] ASC)
INCLUDE ([TC], [ProgramID])
WHERE [ApplicationDate] IS NOT NULL
WITH (PAD_INDEX = ON, FILLFACTOR = 70, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = OFF);

CREATE INDEX [IX_Legacy_History_ChangeDate_Disabled]
ON [dbo].[ApplicationStatusHistory] ([ChangeDate] ASC);
ALTER INDEX [IX_Legacy_History_ChangeDate_Disabled] ON [dbo].[ApplicationStatusHistory] DISABLE;

ALTER TABLE [dbo].[PasswordResetTokens] ALTER COLUMN [TC] char(11) NULL;
CREATE INDEX [IX_Legacy_PasswordResetTokens_TC_AlreadyTarget]
ON [dbo].[PasswordResetTokens] ([TC] ASC)
WITH (FILLFACTOR = 77);
EXEC sys.sp_addextendedproperty
    @name = N'MigrationTestMarker',
    @value = N'index-was-not-recreated',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'PasswordResetTokens',
    @level2type = N'INDEX', @level2name = N'IX_Legacy_PasswordResetTokens_TC_AlreadyTarget';
"@
    Invoke-EfDatabaseUpdate -Database $successDatabase -ExpectSuccess $true
    Assert-SuccessScenario -Database $successDatabase

    $duplicateDatabase = New-TestDatabase -Scenario 'Duplicate'
    Initialize-LegacySchema -Database $duplicateDatabase -SeedSql @"
INSERT INTO [dbo].[Students] ([TC], [Email], [PasswordHash]) VALUES ('00000000000', N'student@example.test', 'legacy-hash');
INSERT INTO [dbo].[Programs] ([ProgramName]) VALUES (N'Program 1');
INSERT INTO [dbo].[Applications] ([TC], [ProgramID], [CurrentStatus]) VALUES ('00000000000', 1, N'Pending');
INSERT INTO [dbo].[Applications] ([TC], [ProgramID], [CurrentStatus]) VALUES ('00000000000', 1, N'Pending');
"@
    Invoke-EfDatabaseUpdate -Database $duplicateDatabase -ExpectSuccess $false
    Assert-PreflightRollback -Database $duplicateDatabase

    $invalidStatusDatabase = New-TestDatabase -Scenario 'InvalidStatus'
    Initialize-LegacySchema -Database $invalidStatusDatabase -SeedSql @"
INSERT INTO [dbo].[Students] ([TC], [Email], [PasswordHash]) VALUES ('00000000000', N'student@example.test', 'legacy-hash');
INSERT INTO [dbo].[Programs] ([ProgramName]) VALUES (N'Program 1');
DECLARE @invalidSeedCheck sysname;
DECLARE @invalidSeedCheckSql nvarchar(max);
SELECT TOP (1) @invalidSeedCheck = [name]
FROM sys.check_constraints
WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[Applications]');
IF @invalidSeedCheck IS NOT NULL
BEGIN
    SET @invalidSeedCheckSql = N'ALTER TABLE [dbo].[Applications] DROP CONSTRAINT ' + QUOTENAME(@invalidSeedCheck) + N';';
    EXEC sys.sp_executesql @invalidSeedCheckSql;
END;
INSERT INTO [dbo].[Applications] ([TC], [ProgramID], [CurrentStatus]) VALUES ('00000000000', 1, N'UnexpectedStatus');
"@
    Invoke-EfDatabaseUpdate -Database $invalidStatusDatabase -ExpectSuccess $false
    Assert-PreflightRollback -Database $invalidStatusDatabase

    $primaryKeyDatabase = New-TestDatabase -Scenario 'PrimaryKeyDependency'
    Initialize-LegacySchema -Database $primaryKeyDatabase -SeedSql @"
INSERT INTO [dbo].[Admins] ([Email], [PasswordHash]) VALUES (N'admin@example.test', 'legacy-hash');
ALTER TABLE [dbo].[Admins] DROP CONSTRAINT [PK_Admins];
ALTER TABLE [dbo].[Admins] ADD CONSTRAINT [PK_Legacy_Admins_Email] PRIMARY KEY NONCLUSTERED ([Email]);
"@
    Invoke-EfDatabaseUpdate -Database $primaryKeyDatabase -ExpectSuccess $false -ExpectedErrorContains 'PK_Legacy_Admins_Email'
    Assert-PreflightRollback -Database $primaryKeyDatabase
    Assert-KeyConstraintPreserved -Database $primaryKeyDatabase -ConstraintType 'PK' -ConstraintName 'PK_Legacy_Admins_Email'

    $uniqueConstraintDatabase = New-TestDatabase -Scenario 'UniqueConstraintDependency'
    Initialize-LegacySchema -Database $uniqueConstraintDatabase -SeedSql @"
INSERT INTO [dbo].[Admins] ([Email], [PasswordHash]) VALUES (N'admin@example.test', 'legacy-hash');
ALTER TABLE [dbo].[Admins] ADD CONSTRAINT [UQ_Legacy_Admins_Email] UNIQUE NONCLUSTERED ([Email]);
"@
    Invoke-EfDatabaseUpdate -Database $uniqueConstraintDatabase -ExpectSuccess $false -ExpectedErrorContains 'UQ_Legacy_Admins_Email'
    Assert-PreflightRollback -Database $uniqueConstraintDatabase
    Assert-KeyConstraintPreserved -Database $uniqueConstraintDatabase -ConstraintType 'UQ' -ConstraintName 'UQ_Legacy_Admins_Email'

    $managedIndexMismatchDatabase = New-TestDatabase -Scenario 'ManagedIndexMismatch'
    Initialize-LegacySchema -Database $managedIndexMismatchDatabase -SeedSql @"
INSERT INTO [dbo].[Admins] ([Email], [PasswordHash]) VALUES (N'admin@example.test', 'legacy-hash');
ALTER TABLE [dbo].[Admins] ADD [NormalizedEmail] nvarchar(254) NULL;
UPDATE [dbo].[Admins] SET [NormalizedEmail] = UPPER([Email]);
ALTER TABLE [dbo].[Admins] ALTER COLUMN [NormalizedEmail] nvarchar(254) NOT NULL;
CREATE INDEX [IX_Admins_NormalizedEmail] ON [dbo].[Admins] ([NormalizedEmail]);
"@
    Invoke-EfDatabaseUpdate -Database $managedIndexMismatchDatabase -ExpectSuccess $false -ExpectedErrorContains 'IX_Admins_NormalizedEmail'
    Assert-RejectedMigrationNotRecorded -Database $managedIndexMismatchDatabase

    Write-Output 'HardenExistingSchema LocalDB integration validation passed.'
}
finally {
    [Environment]::SetEnvironmentVariable('ConnectionStrings__DefaultConnection', $previousConnectionString, 'Process')

    foreach ($databaseName in $createdDatabases) {
        if (-not [Regex]::IsMatch(
            $databaseName,
            '^GraduateAppMigrationTest_[A-Za-z0-9_]+$',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            Write-Warning "Refusing to drop unexpected database name '$databaseName'."
            continue
        }

        try {
            $quotedDatabase = Quote-SqlIdentifier $databaseName
            Invoke-Sql -Database 'master' -Query "IF DB_ID(N'$databaseName') IS NOT NULL BEGIN ALTER DATABASE $quotedDatabase SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE $quotedDatabase; END;"
        }
        catch {
            Write-Warning "Could not remove temporary database '$databaseName': $($_.Exception.Message)"
        }
    }
}
