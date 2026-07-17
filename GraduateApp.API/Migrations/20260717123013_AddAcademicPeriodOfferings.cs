using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GraduateApp.API.Migrations;

/// <inheritdoc />
public partial class AddAcademicPeriodOfferings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET XACT_ABORT ON;

            IF OBJECT_ID(N'[dbo].[Programs]', N'U') IS NULL
                OR OBJECT_ID(N'[dbo].[Applications]', N'U') IS NULL
                OR OBJECT_ID(N'[dbo].[Students]', N'U') IS NULL
                OR COL_LENGTH(N'dbo.Programs', N'ProgramID') IS NULL
                OR COL_LENGTH(N'dbo.Programs', N'IsOpen') IS NULL
                OR COL_LENGTH(N'dbo.Programs', N'ApplicationDeadlineUtc') IS NULL
                OR COL_LENGTH(N'dbo.Applications', N'ApplicationID') IS NULL
                OR COL_LENGTH(N'dbo.Applications', N'ProgramID') IS NULL
                OR COL_LENGTH(N'dbo.Applications', N'TC') IS NULL
            BEGIN
                THROW 51200, N'Program ilanı dönüşümü için gereken legacy GraduateApp şeması bulunamadı; migration durduruldu.', 1;
            END;

            IF EXISTS
            (
                SELECT 1
                FROM [dbo].[Applications] AS a
                LEFT JOIN [dbo].[Programs] AS p ON p.[ProgramID] = a.[ProgramID]
                WHERE p.[ProgramID] IS NULL
            )
            BEGIN
                THROW 51201, N'Bir veya daha fazla legacy başvuru geçerli bir programa bağlı değil; veri değiştirilmeden migration durduruldu.', 1;
            END;
            """);

        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            table: "Programs",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.CreateTable(
            name: "ProgramOfferings",
            columns: table => new
            {
                ProgramOfferingID = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProgramID = table.Column<int>(type: "int", nullable: false),
                AcademicYearStart = table.Column<int>(type: "int", nullable: false),
                Term = table.Column<int>(type: "int", nullable: false),
                ApplicationStartUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ApplicationDeadlineUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                Quota = table.Column<int>(type: "int", nullable: false),
                IsOpen = table.Column<bool>(type: "bit", nullable: false),
                IsArchived = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProgramOfferings", x => x.ProgramOfferingID);
                table.CheckConstraint(
                    "CK_ProgramOfferings_AcademicPeriod",
                    "([AcademicYearStart] = 0 AND [Term] = 0) OR ([AcademicYearStart] BETWEEN 2000 AND 2200 AND [Term] IN (1,2,3))");
                table.CheckConstraint(
                    "CK_ProgramOfferings_DateRange",
                    "[ApplicationStartUtc] IS NULL OR [ApplicationDeadlineUtc] IS NULL OR [ApplicationStartUtc] < [ApplicationDeadlineUtc]");
                table.CheckConstraint("CK_ProgramOfferings_Quota", "[Quota] >= 0");
                table.ForeignKey(
                    name: "FK_ProgramOfferings_Programs_ProgramID",
                    column: x => x.ProgramID,
                    principalTable: "Programs",
                    principalColumn: "ProgramID",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProgramOfferings_ProgramID_AcademicYearStart_Term",
            table: "ProgramOfferings",
            columns: new[] { "ProgramID", "AcademicYearStart", "Term" },
            unique: true);

        migrationBuilder.AddColumn<int>(
            name: "ProgramOfferingID",
            table: "Applications",
            type: "int",
            nullable: true);

        // Academic year and term cannot be inferred from the legacy schema. Every permanent
        // program therefore receives one closed, archived and explicitly unidentified offering.
        migrationBuilder.Sql(
            """
            INSERT INTO [dbo].[ProgramOfferings]
                ([ProgramID], [AcademicYearStart], [Term], [ApplicationStartUtc],
                 [ApplicationDeadlineUtc], [Quota], [IsOpen], [IsArchived], [CreatedAtUtc], [UpdatedAtUtc])
            SELECT p.[ProgramID], 0, 0, NULL,
                   p.[ApplicationDeadlineUtc], 0, CAST(0 AS bit), CAST(1 AS bit),
                   SYSUTCDATETIME(), SYSUTCDATETIME()
            FROM [dbo].[Programs] AS p;
            """);

        // ProgramOfferingID is added by the previous DbCommand, so it is referenced only here.
        migrationBuilder.Sql(
            """
            EXEC sys.sp_executesql N'
                DECLARE @legacyApplicationCount bigint =
                    (SELECT COUNT_BIG(*) FROM [dbo].[Applications]);

                UPDATE a
                SET [ProgramOfferingID] = po.[ProgramOfferingID]
                FROM [dbo].[Applications] AS a
                INNER JOIN [dbo].[ProgramOfferings] AS po
                    ON po.[ProgramID] = a.[ProgramID]
                   AND po.[AcademicYearStart] = 0
                   AND po.[Term] = 0;

                DECLARE @mappedApplicationCount bigint =
                    (SELECT COUNT_BIG(*) FROM [dbo].[Applications] WHERE [ProgramOfferingID] IS NOT NULL);

                IF @mappedApplicationCount <> @legacyApplicationCount
                    OR EXISTS (SELECT 1 FROM [dbo].[Applications] WHERE [ProgramOfferingID] IS NULL)
                BEGIN
                    THROW 51202, N''Legacy başvuruların tamamı dönemsel ilana eşlenemedi; migration transactionı geri alınacaktır.'', 1;
                END;';
            """);

        // Keep the legacy reporting view operational before removing Applications.ProgramID.
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'[dbo].[vw_AdminApplicationSummary]', N'V') IS NOT NULL
            BEGIN
                IF OBJECTPROPERTYEX(OBJECT_ID(N'[dbo].[vw_AdminApplicationSummary]'), N'IsSchemaBound') = 1
                    OR EXISTS
                    (
                        SELECT 1
                        FROM sys.indexes
                        WHERE [object_id] = OBJECT_ID(N'[dbo].[vw_AdminApplicationSummary]')
                          AND [index_id] > 0
                    )
                BEGIN
                    THROW 51204, N'vw_AdminApplicationSummary şema bağlı veya indeksli; görünüm semantiği otomatik değiştirilemediği için migration durduruldu.', 1;
                END;

                EXEC sys.sp_executesql N'
                    ALTER VIEW [dbo].[vw_AdminApplicationSummary]
                    AS
                    SELECT
                        a.[ApplicationID] AS [BasvuruNo],
                        s.[TC] AS [KimlikNo],
                        CONCAT(s.[StudentName], N'' '', s.[StudentSurname]) AS [AdSoyad],
                        s.[Email] AS [Eposta],
                        i.[InstituteName] AS [Enstitu],
                        p.[ProgramName] AS [Program],
                        education.[UniversityName] AS [MezunOlduguUniversite],
                        education.[GNO] AS [LisansOrtalamasi],
                        a.[ApplicationDate] AS [BasvuruTarihi],
                        a.[CurrentStatus] AS [GuncelDurum]
                    FROM [dbo].[Applications] AS a
                    INNER JOIN [dbo].[Students] AS s ON s.[TC] = a.[TC]
                    INNER JOIN [dbo].[ProgramOfferings] AS po ON po.[ProgramOfferingID] = a.[ProgramOfferingID]
                    INNER JOIN [dbo].[Programs] AS p ON p.[ProgramID] = po.[ProgramID]
                    INNER JOIN [dbo].[Institutes] AS i ON i.[InstituteID] = p.[InstituteID]
                    OUTER APPLY
                    (
                        SELECT TOP (1) u.[UniversityName], e.[GNO]
                        FROM [dbo].[EducationInfo] AS e
                        LEFT JOIN [dbo].[Universities] AS u ON u.[UniversityID] = e.[UniversityID]
                        WHERE e.[TC] = a.[TC]
                        ORDER BY e.[EducationID] DESC
                    ) AS education;';
            END;
            """);

        // Remove all metadata dependencies of the legacy column by discovered, quoted names.
        // Key constraints are intentionally not converted to indexes because that would lose semantics.
        migrationBuilder.Sql(
            """
            DECLARE @applicationsObjectId int = OBJECT_ID(N'[dbo].[Applications]', N'U');
            DECLARE @legacyProgramColumnId int = COLUMNPROPERTY(@applicationsObjectId, N'ProgramID', 'ColumnId');

            DECLARE @blockingConstraint sysname =
            (
                SELECT TOP (1) kc.[name]
                FROM sys.key_constraints AS kc
                INNER JOIN sys.index_columns AS ic
                    ON ic.[object_id] = kc.[parent_object_id]
                   AND ic.[index_id] = kc.[unique_index_id]
                WHERE kc.[parent_object_id] = @applicationsObjectId
                  AND ic.[column_id] = @legacyProgramColumnId
                ORDER BY kc.[name]
            );
            IF @blockingConstraint IS NOT NULL
            BEGIN
                DECLARE @constraintMessage nvarchar(2048) =
                    N'Applications.ProgramID kolonu kaldırılırken primary/unique constraint semantiği korunamıyor: '
                    + QUOTENAME(@blockingConstraint) + N'. Migration durduruldu.';
                THROW 51203, @constraintMessage, 1;
            END;

            DECLARE @dropSql nvarchar(max);

            SELECT @dropSql = STRING_AGG(
                CONVERT(nvarchar(max), N'ALTER TABLE [dbo].[Applications] DROP CONSTRAINT ' + QUOTENAME(fk.[name]) + N';'),
                CHAR(10))
            FROM sys.foreign_keys AS fk
            WHERE fk.[parent_object_id] = @applicationsObjectId
              AND EXISTS
              (
                  SELECT 1
                  FROM sys.foreign_key_columns AS fkc
                  WHERE fkc.[constraint_object_id] = fk.[object_id]
                    AND fkc.[parent_column_id] = @legacyProgramColumnId
              );
            IF @dropSql IS NOT NULL
                EXEC sys.sp_executesql @dropSql;

            SET @dropSql = NULL;
            SELECT @dropSql = STRING_AGG(
                CONVERT(nvarchar(max), N'DROP INDEX ' + QUOTENAME(i.[name]) + N' ON [dbo].[Applications];'),
                CHAR(10))
            FROM sys.indexes AS i
            WHERE i.[object_id] = @applicationsObjectId
              AND i.[name] IS NOT NULL
              AND i.[is_primary_key] = 0
              AND i.[is_unique_constraint] = 0
              AND EXISTS
              (
                  SELECT 1
                  FROM sys.index_columns AS ic
                  WHERE ic.[object_id] = i.[object_id]
                    AND ic.[index_id] = i.[index_id]
                    AND ic.[column_id] = @legacyProgramColumnId
              );
            IF @dropSql IS NOT NULL
                EXEC sys.sp_executesql @dropSql;

            DECLARE @defaultConstraint sysname =
            (
                SELECT dc.[name]
                FROM sys.default_constraints AS dc
                WHERE dc.[parent_object_id] = @applicationsObjectId
                  AND dc.[parent_column_id] = @legacyProgramColumnId
            );
            IF @defaultConstraint IS NOT NULL
            BEGIN
                SET @dropSql = N'ALTER TABLE [dbo].[Applications] DROP CONSTRAINT '
                    + QUOTENAME(@defaultConstraint) + N';';
                EXEC sys.sp_executesql @dropSql;
            END;

            ALTER TABLE [dbo].[Applications] DROP COLUMN [ProgramID];
            """);

        // Dynamic batches keep idempotent scripts safe when SQL Server compiles the whole
        // migration before the newly added ProgramOfferingID column exists.
        migrationBuilder.Sql(
            """
            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Applications] ALTER COLUMN [ProgramOfferingID] int NOT NULL;';
            """);

        migrationBuilder.Sql(
            """
            EXEC sys.sp_executesql N'CREATE INDEX [IX_Applications_ProgramOfferingID]
                ON [dbo].[Applications] ([ProgramOfferingID]);';
            """);

        migrationBuilder.Sql(
            """
            EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_Applications_TC_ProgramOfferingID]
                ON [dbo].[Applications] ([TC], [ProgramOfferingID]);';
            """);

        migrationBuilder.Sql(
            """
            EXEC sys.sp_executesql N'ALTER TABLE [dbo].[Applications]
                ADD CONSTRAINT [FK_Applications_ProgramOfferings_ProgramOfferingID]
                FOREIGN KEY ([ProgramOfferingID]) REFERENCES [dbo].[ProgramOfferings] ([ProgramOfferingID]);';
            """);

        migrationBuilder.CreateTable(
            name: "ApplicationScoreSnapshots",
            columns: table => new
            {
                ApplicationScoreSnapshotID = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ApplicationID = table.Column<int>(type: "int", nullable: false),
                ExamID = table.Column<int>(type: "int", nullable: false),
                ExamNameSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                ScoreSnapshot = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                ExamDateSnapshot = table.Column<DateOnly>(type: "date", nullable: true),
                CapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApplicationScoreSnapshots", x => x.ApplicationScoreSnapshotID);
                table.ForeignKey(
                    name: "FK_ApplicationScoreSnapshots_Applications_ApplicationID",
                    column: x => x.ApplicationID,
                    principalTable: "Applications",
                    principalColumn: "ApplicationID",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ApplicationScoreSnapshots_Exams_ExamID",
                    column: x => x.ExamID,
                    principalTable: "Exams",
                    principalColumn: "ExamID",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ProgramOfferingExamRequirements",
            columns: table => new
            {
                RequirementID = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProgramOfferingID = table.Column<int>(type: "int", nullable: false),
                ExamID = table.Column<int>(type: "int", nullable: false),
                MinimumScore = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                MinimumValidityDate = table.Column<DateOnly>(type: "date", nullable: true),
                IsRequired = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProgramOfferingExamRequirements", x => x.RequirementID);
                table.CheckConstraint(
                    "CK_ProgramOfferingExamRequirements_MinimumScore",
                    "[MinimumScore] >= 0");
                table.ForeignKey(
                    name: "FK_ProgramOfferingExamRequirements_Exams_ExamID",
                    column: x => x.ExamID,
                    principalTable: "Exams",
                    principalColumn: "ExamID",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ProgramOfferingExamRequirements_ProgramOfferings_ProgramOfferingID",
                    column: x => x.ProgramOfferingID,
                    principalTable: "ProgramOfferings",
                    principalColumn: "ProgramOfferingID",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ApplicationScoreSnapshots_ApplicationID_ExamID",
            table: "ApplicationScoreSnapshots",
            columns: new[] { "ApplicationID", "ExamID" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ApplicationScoreSnapshots_ExamID",
            table: "ApplicationScoreSnapshots",
            column: "ExamID");

        migrationBuilder.CreateIndex(
            name: "IX_ProgramOfferingExamRequirements_ExamID",
            table: "ProgramOfferingExamRequirements",
            column: "ExamID");

        migrationBuilder.CreateIndex(
            name: "IX_ProgramOfferingExamRequirements_ProgramOfferingID_ExamID",
            table: "ProgramOfferingExamRequirements",
            columns: new[] { "ProgramOfferingID", "ExamID" },
            unique: true);

        // Legacy values have already been copied to the archived offering.
        migrationBuilder.DropColumn(
            name: "ApplicationDeadlineUtc",
            table: "Programs");

        migrationBuilder.DropColumn(
            name: "IsOpen",
            table: "Programs");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "Program offering ve immutable puan snapshot verileri otomatik geri dönüşte güvenle birleştirilemez. Doğrulanmış yedek ve manuel DBA planı gerekir.");
    }
}
