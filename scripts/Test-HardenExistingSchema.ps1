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
        [Parameter(Mandatory)][bool]$ExpectSuccess
    )

    [Environment]::SetEnvironmentVariable(
        'ConnectionStrings__DefaultConnection',
        "Server=$SqlServer;Database=$Database;Integrated Security=true;TrustServerCertificate=true",
        'Process')

    Push-Location $repositoryRoot
    try {
        $output = & dotnet ef database update --project GraduateApp.API --startup-project GraduateApp.API --no-build 2>&1
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
}

function Assert-SuccessScenario {
    param([Parameter(Mandatory)][string]$Database)

    $assertions = @"
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

try {
    $successDatabase = New-TestDatabase -Scenario 'Success'
    Initialize-LegacySchema -Database $successDatabase -SeedSql @"
INSERT INTO [dbo].[Admins] ([Email], [PasswordHash]) VALUES (N'admin@example.test', 'legacy-hash');
INSERT INTO [dbo].[Students] ([TC], [Email], [PasswordHash]) VALUES ('00000000000', N'student@example.test', 'legacy-hash');
INSERT INTO [dbo].[Programs] ([ProgramName]) VALUES (N'Program 1');
INSERT INTO [dbo].[Applications] ([TC], [ProgramID], [ApplicationDate], [CurrentStatus]) VALUES ('00000000000', 1, NULL, N'Pending');
INSERT INTO [dbo].[ApplicationStatusHistory] ([ApplicationID], [StatusName], [ChangeDate], [Notes]) VALUES (1, N'Pending', NULL, N'legacy');
INSERT INTO [dbo].[PasswordResetTokens] ([TC], [TokenHash], [ExpirationDate], [IsUsed]) VALUES ('00000000000', 'legacy-token', DATEADD(hour, 1, GETDATE()), NULL);
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
