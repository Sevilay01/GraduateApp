using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GraduateApp.API.Migrations
{
    /// <inheritdoc />
    public partial class SecureApplicationDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[Applications]', N'U') IS NULL
                   OR OBJECT_ID(N'[dbo].[ProgramOfferings]', N'U') IS NULL
                   OR OBJECT_ID(N'[dbo].[Admins]', N'U') IS NULL
                   OR OBJECT_ID(N'[dbo].[SecurityAuditLogs]', N'U') IS NULL
                    THROW 51500, N'Güvenli belge migration ön kontrolü başarısız: beklenen temel tablolardan biri bulunamadı.', 1;

                IF COL_LENGTH(N'dbo.Applications', N'ApplicationID') IS NULL
                   OR COL_LENGTH(N'dbo.Applications', N'TC') IS NULL
                   OR COL_LENGTH(N'dbo.Applications', N'ProgramOfferingID') IS NULL
                   OR COL_LENGTH(N'dbo.Applications', N'CurrentStatus') IS NULL
                   OR COL_LENGTH(N'dbo.ProgramOfferings', N'ProgramOfferingID') IS NULL
                   OR COL_LENGTH(N'dbo.Admins', N'AdminID') IS NULL
                    THROW 51501, N'Güvenli belge migration ön kontrolü başarısız: beklenen temel kolonlardan biri bulunamadı.', 1;

                IF OBJECT_ID(N'[dbo].[ProgramOfferingDocumentRequirements]', N'U') IS NOT NULL
                   OR OBJECT_ID(N'[dbo].[ApplicationDocumentRequirementSnapshots]', N'U') IS NOT NULL
                   OR OBJECT_ID(N'[dbo].[ApplicationDocuments]', N'U') IS NOT NULL
                   OR COL_LENGTH(N'dbo.Applications', N'PublicID') IS NOT NULL
                   OR COL_LENGTH(N'dbo.Applications', N'UsesDocumentWorkflow') IS NOT NULL
                    THROW 51502, N'Güvenli belge migration ön kontrolü başarısız: hedef tablo veya kolonlardan biri migration geçmişi dışında zaten mevcut.', 1;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM [sys].[check_constraints]
                    WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[Applications]')
                      AND [name] = N'CK_Applications_CurrentStatus'
                )
                    THROW 51503, N'Güvenli belge migration ön kontrolü başarısız: başvuru durum constraint kaydı bulunamadı.', 1;

                IF EXISTS
                (
                    SELECT 1 FROM [dbo].[Applications]
                    WHERE [CurrentStatus] NOT IN (N'Pending',N'UnderReview',N'Approved',N'Rejected',N'Withdrawn')
                )
                    THROW 51504, N'Güvenli belge migration ön kontrolü başarısız: tanımsız başvuru durumu bulundu. Veri otomatik değiştirilmedi.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[Applications]
                    GROUP BY [TC], [ProgramOfferingID]
                    HAVING COUNT_BIG(*) > 1
                )
                    THROW 51505, N'Güvenli belge migration ön kontrolü başarısız: aynı öğrenci ve ilan için yinelenen başvuru bulundu. Veri otomatik birleştirilmedi.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Applications_CurrentStatus",
                table: "Applications");

            migrationBuilder.AddColumn<Guid>(
                name: "PublicID",
                table: "Applications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UsesDocumentWorkflow",
                table: "Applications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE [dbo].[Applications]
                SET [PublicID] = NEWID()
                WHERE [PublicID] IS NULL;

                IF EXISTS (SELECT 1 FROM [dbo].[Applications] WHERE [PublicID] IS NULL)
                    THROW 51506, N'Başvuru PublicID alanı güvenli biçimde doldurulamadı.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[Applications]
                    GROUP BY [PublicID]
                    HAVING COUNT_BIG(*) > 1
                )
                    THROW 51507, N'Başvuru PublicID alanında beklenmeyen yinelenen değer oluştu.', 1;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "PublicID",
                table: "Applications",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE [dbo].[Applications]
                    ADD CONSTRAINT [DF_Applications_PublicID] DEFAULT (NEWID()) FOR [PublicID];
                """);

            migrationBuilder.CreateTable(
                name: "ProgramOfferingDocumentRequirements",
                columns: table => new
                {
                    RequirementID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicID = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    ProgramOfferingID = table.Column<int>(type: "int", nullable: false),
                    DocumentCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    NormalizedDocumentCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AllowedContentCategory = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    MaximumBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramOfferingDocumentRequirements", x => x.RequirementID);
                    table.CheckConstraint("CK_ProgramOfferingDocumentRequirements_ContentCategory", "[AllowedContentCategory] IN ('PdfOnly','ImageOnly','PdfOrImage')");
                    table.CheckConstraint("CK_ProgramOfferingDocumentRequirements_MaximumBytes", "[MaximumBytes] BETWEEN 1 AND 104857600");
                    table.ForeignKey(
                        name: "FK_ProgramOfferingDocumentRequirements_ProgramOfferings_ProgramOfferingID",
                        column: x => x.ProgramOfferingID,
                        principalTable: "ProgramOfferings",
                        principalColumn: "ProgramOfferingID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationDocumentRequirementSnapshots",
                columns: table => new
                {
                    SnapshotID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicID = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    ApplicationID = table.Column<int>(type: "int", nullable: false),
                    SourceRequirementID = table.Column<int>(type: "int", nullable: true),
                    DocumentCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    AllowedContentCategory = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    MaximumBytes = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationDocumentRequirementSnapshots", x => x.SnapshotID);
                    table.CheckConstraint("CK_ApplicationDocumentRequirementSnapshots_ContentCategory", "[AllowedContentCategory] IN ('PdfOnly','ImageOnly','PdfOrImage')");
                    table.CheckConstraint("CK_ApplicationDocumentRequirementSnapshots_MaximumBytes", "[MaximumBytes] BETWEEN 1 AND 104857600");
                    table.ForeignKey(
                        name: "FK_ApplicationDocumentRequirementSnapshots_Applications_ApplicationID",
                        column: x => x.ApplicationID,
                        principalTable: "Applications",
                        principalColumn: "ApplicationID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApplicationDocumentRequirementSnapshots_ProgramOfferingDocumentRequirements_SourceRequirementID",
                        column: x => x.SourceRequirementID,
                        principalTable: "ProgramOfferingDocumentRequirements",
                        principalColumn: "RequirementID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationDocuments",
                columns: table => new
                {
                    DocumentID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicID = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    ApplicationID = table.Column<int>(type: "int", nullable: false),
                    RequirementSnapshotID = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ObjectKey = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    VerifiedContentType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ReviewStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByAdminID = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationDocuments", x => x.DocumentID);
                    table.CheckConstraint("CK_ApplicationDocuments_ContentType", "[VerifiedContentType] IN ('application/pdf','image/jpeg','image/png')");
                    table.CheckConstraint("CK_ApplicationDocuments_FileSize", "[FileSize] > 0");
                    table.CheckConstraint("CK_ApplicationDocuments_ObjectKey", "LEN([ObjectKey]) = 32 AND [ObjectKey] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_ApplicationDocuments_Review", "([ReviewStatus] = 'Rejected' AND [RejectionReason] IS NOT NULL AND LEN(LTRIM(RTRIM([RejectionReason]))) > 0) OR ([ReviewStatus] IN ('Pending','Approved') AND [RejectionReason] IS NULL)");
                    table.CheckConstraint("CK_ApplicationDocuments_Sha256", "LEN([Sha256]) = 64 AND [Sha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_ApplicationDocuments_VersionNumber", "[VersionNumber] > 0");
                    table.ForeignKey(
                        name: "FK_ApplicationDocuments_Admins_ReviewedByAdminID",
                        column: x => x.ReviewedByAdminID,
                        principalTable: "Admins",
                        principalColumn: "AdminID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApplicationDocuments_ApplicationDocumentRequirementSnapshots_RequirementSnapshotID",
                        column: x => x.RequirementSnapshotID,
                        principalTable: "ApplicationDocumentRequirementSnapshots",
                        principalColumn: "SnapshotID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApplicationDocuments_Applications_ApplicationID",
                        column: x => x.ApplicationID,
                        principalTable: "Applications",
                        principalColumn: "ApplicationID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_PublicID",
                table: "Applications",
                column: "PublicID",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Applications_CurrentStatus",
                table: "Applications",
                sql: "[CurrentStatus] IN (N'Draft',N'Pending',N'UnderReview',N'Approved',N'Rejected',N'Withdrawn')");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocumentRequirementSnapshots_ApplicationID_DocumentCode",
                table: "ApplicationDocumentRequirementSnapshots",
                columns: new[] { "ApplicationID", "DocumentCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocumentRequirementSnapshots_PublicID",
                table: "ApplicationDocumentRequirementSnapshots",
                column: "PublicID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocumentRequirementSnapshots_SourceRequirementID",
                table: "ApplicationDocumentRequirementSnapshots",
                column: "SourceRequirementID");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocuments_ApplicationID",
                table: "ApplicationDocuments",
                column: "ApplicationID");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocuments_ObjectKey",
                table: "ApplicationDocuments",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocuments_PublicID",
                table: "ApplicationDocuments",
                column: "PublicID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocuments_RequirementSnapshotID",
                table: "ApplicationDocuments",
                column: "RequirementSnapshotID",
                unique: true,
                filter: "[IsCurrent] = CAST(1 AS bit)");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocuments_RequirementSnapshotID_VersionNumber",
                table: "ApplicationDocuments",
                columns: new[] { "RequirementSnapshotID", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocuments_ReviewedByAdminID",
                table: "ApplicationDocuments",
                column: "ReviewedByAdminID");

            migrationBuilder.CreateIndex(
                name: "IX_ProgramOfferingDocumentRequirements_ProgramOfferingID_NormalizedDocumentCode",
                table: "ProgramOfferingDocumentRequirements",
                columns: new[] { "ProgramOfferingID", "NormalizedDocumentCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgramOfferingDocumentRequirements_PublicID",
                table: "ProgramOfferingDocumentRequirements",
                column: "PublicID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "SecureApplicationDocuments migration geri alınamaz; belge geçmişi ve sahiplik metadata alanlarını kaldırmak veri kaybına yol açabilir.");
        }
    }
}
