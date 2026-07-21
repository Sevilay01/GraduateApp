using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GraduateApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddInstituteProgramAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[Institutes]', N'U') IS NULL
                    THROW 51300, N'Enstitü/program yönetimi migration ön kontrolü başarısız: Institutes tablosu bulunamadı.', 1;

                IF OBJECT_ID(N'[dbo].[Programs]', N'U') IS NULL
                    THROW 51301, N'Enstitü/program yönetimi migration ön kontrolü başarısız: Programs tablosu bulunamadı.', 1;

                IF COL_LENGTH(N'dbo.Institutes', N'InstituteID') IS NULL
                   OR COL_LENGTH(N'dbo.Institutes', N'InstituteName') IS NULL
                   OR COL_LENGTH(N'dbo.Programs', N'ProgramID') IS NULL
                   OR COL_LENGTH(N'dbo.Programs', N'InstituteID') IS NULL
                   OR COL_LENGTH(N'dbo.Programs', N'ProgramName') IS NULL
                   OR COL_LENGTH(N'dbo.Programs', N'DegreeType') IS NULL
                    THROW 51302, N'Enstitü/program yönetimi migration ön kontrolü başarısız: beklenen kolonlardan biri bulunamadı.', 1;

                DECLARE @InvalidInstituteCount bigint =
                (
                    SELECT COUNT_BIG(*)
                    FROM [dbo].[Institutes]
                    WHERE [InstituteName] IS NULL
                       OR [InstituteName] <> LTRIM(RTRIM([InstituteName]))
                       OR LEN([InstituteName]) < 2
                       OR LEN([InstituteName]) > 100
                );

                IF @InvalidInstituteCount > 0
                BEGIN
                    DECLARE @InvalidInstituteMessage nvarchar(2048) = CONCAT(
                        N'Enstitü/program yönetimi migration ön kontrolü başarısız: ',
                        @InvalidInstituteCount,
                        N' enstitü adı boş, kırpılmamış veya uzunluk sınırı dışında. Veri otomatik düzeltilmedi.');
                    THROW 51303, @InvalidInstituteMessage, 1;
                END;

                DECLARE @DuplicateInstituteCount bigint =
                (
                    SELECT COUNT_BIG(*)
                    FROM
                    (
                        SELECT [InstituteName]
                        FROM [dbo].[Institutes]
                        GROUP BY [InstituteName]
                        HAVING COUNT_BIG(*) > 1
                    ) AS [DuplicateInstitutes]
                );

                IF @DuplicateInstituteCount > 0
                BEGIN
                    DECLARE @DuplicateInstituteMessage nvarchar(2048) = CONCAT(
                        N'Enstitü/program yönetimi migration ön kontrolü başarısız: ',
                        @DuplicateInstituteCount,
                        N' mükerrer enstitü adı grubu bulundu. Veri otomatik birleştirilmedi.');
                    THROW 51304, @DuplicateInstituteMessage, 1;
                END;

                DECLARE @InvalidProgramCount bigint =
                (
                    SELECT COUNT_BIG(*)
                    FROM [dbo].[Programs]
                    WHERE [ProgramName] IS NULL
                       OR [ProgramName] <> LTRIM(RTRIM([ProgramName]))
                       OR LEN([ProgramName]) < 2
                       OR LEN([ProgramName]) > 100
                       OR [DegreeType] IS NULL
                       OR [DegreeType] NOT IN
                          (N'Doktora', N'Tezli Yüksek Lisans', N'Tezsiz Yüksek Lisans', N'Uzaktan Tezsiz Yüksek Lisans')
                );

                IF @InvalidProgramCount > 0
                BEGIN
                    DECLARE @InvalidProgramMessage nvarchar(2048) = CONCAT(
                        N'Enstitü/program yönetimi migration ön kontrolü başarısız: ',
                        @InvalidProgramCount,
                        N' program adı veya derece türü geçersiz. Veri otomatik düzeltilmedi.');
                    THROW 51305, @InvalidProgramMessage, 1;
                END;

                DECLARE @DuplicateProgramCount bigint =
                (
                    SELECT COUNT_BIG(*)
                    FROM
                    (
                        SELECT [InstituteID], [ProgramName], [DegreeType]
                        FROM [dbo].[Programs]
                        GROUP BY [InstituteID], [ProgramName], [DegreeType]
                        HAVING COUNT_BIG(*) > 1
                    ) AS [DuplicatePrograms]
                );

                IF @DuplicateProgramCount > 0
                BEGIN
                    DECLARE @DuplicateProgramMessage nvarchar(2048) = CONCAT(
                        N'Enstitü/program yönetimi migration ön kontrolü başarısız: ',
                        @DuplicateProgramCount,
                        N' mükerrer (InstituteID, ProgramName, DegreeType) grubu bulundu. Veri otomatik birleştirilmedi.');
                    THROW 51306, @DuplicateProgramMessage, 1;
                END;

                DECLARE @OrphanProgramCount bigint =
                (
                    SELECT COUNT_BIG(*)
                    FROM [dbo].[Programs] AS [program]
                    LEFT JOIN [dbo].[Institutes] AS [institute]
                        ON [program].[InstituteID] = [institute].[InstituteID]
                    WHERE [institute].[InstituteID] IS NULL
                );

                IF @OrphanProgramCount > 0
                BEGIN
                    DECLARE @OrphanProgramMessage nvarchar(2048) = CONCAT(
                        N'Enstitü/program yönetimi migration ön kontrolü başarısız: ',
                        @OrphanProgramCount,
                        N' program geçerli bir enstitüye bağlı değil. Veri otomatik düzeltilmedi.');
                    THROW 51307, @OrphanProgramMessage, 1;
                END;
                """);

            migrationBuilder.Sql(
                """
                IF EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [object_id] = OBJECT_ID(N'[dbo].[Programs]')
                      AND [name] = N'IX_Programs_InstituteID'
                )
                BEGIN
                    DROP INDEX [IX_Programs_InstituteID] ON [dbo].[Programs];
                END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "DegreeType",
                table: "Programs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Programs",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Programs",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Programs",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Institutes",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Institutes",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Institutes",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Institutes",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.CreateIndex(
                name: "IX_Programs_InstituteID_ProgramName_DegreeType",
                table: "Programs",
                columns: new[] { "InstituteID", "ProgramName", "DegreeType" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Programs_DegreeType",
                table: "Programs",
                sql: "[DegreeType] IN (N'Doktora',N'Tezli Yüksek Lisans',N'Tezsiz Yüksek Lisans',N'Uzaktan Tezsiz Yüksek Lisans')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Programs_ProgramName_Trimmed",
                table: "Programs",
                sql: "[ProgramName] = LTRIM(RTRIM([ProgramName])) AND LEN([ProgramName]) >= 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Institutes_InstituteName_Trimmed",
                table: "Institutes",
                sql: "[InstituteName] = LTRIM(RTRIM([InstituteName])) AND LEN([InstituteName]) >= 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "AddInstituteProgramAdministration migration geri alınamaz; audit/eşzamanlılık kolonlarını kaldırmak veri kaybına yol açabilir.");
        }
    }
}
