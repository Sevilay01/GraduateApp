using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GraduateApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationEvaluationAndResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[Applications]', N'U') IS NULL
                   OR OBJECT_ID(N'[dbo].[ProgramOfferings]', N'U') IS NULL
                   OR OBJECT_ID(N'[dbo].[Admins]', N'U') IS NULL
                   OR OBJECT_ID(N'[dbo].[Exams]', N'U') IS NULL
                   OR OBJECT_ID(N'[dbo].[SecurityAuditLogs]', N'U') IS NULL
                   OR OBJECT_ID(N'[dbo].[ProgramOfferingExamRequirements]', N'U') IS NULL
                   OR OBJECT_ID(N'[dbo].[ApplicationDocumentRequirementSnapshots]', N'U') IS NULL
                    THROW 51600, N'Değerlendirme migration ön kontrolü başarısız: beklenen güvenli temel şema bulunamadı.', 1;

                IF COL_LENGTH(N'dbo.Applications', N'ApplicationID') IS NULL
                   OR COL_LENGTH(N'dbo.Applications', N'PublicID') IS NULL
                   OR COL_LENGTH(N'dbo.Applications', N'UsesDocumentWorkflow') IS NULL
                   OR COL_LENGTH(N'dbo.Applications', N'CurrentStatus') IS NULL
                   OR COL_LENGTH(N'dbo.ProgramOfferings', N'ProgramOfferingID') IS NULL
                   OR COL_LENGTH(N'dbo.ProgramOfferings', N'RowVersion') IS NULL
                    THROW 51601, N'Değerlendirme migration ön kontrolü başarısız: beklenen temel kolonlardan biri bulunamadı.', 1;

                IF OBJECT_ID(N'[dbo].[ApplicationEvaluations]', N'U') IS NOT NULL
                   OR OBJECT_ID(N'[dbo].[ApplicationEvaluationComponents]', N'U') IS NOT NULL
                   OR OBJECT_ID(N'[dbo].[ProgramOfferingEvaluationCriteria]', N'U') IS NOT NULL
                   OR COL_LENGTH(N'dbo.Applications', N'UsesEvaluationWorkflow') IS NOT NULL
                   OR COL_LENGTH(N'dbo.ProgramOfferings', N'UsesEvaluationWorkflow') IS NOT NULL
                   OR COL_LENGTH(N'dbo.ProgramOfferings', N'EvaluationState') IS NOT NULL
                    THROW 51602, N'Değerlendirme migration ön kontrolü başarısız: hedef tablo veya kolonlardan biri migration geçmişi dışında zaten mevcut.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[Applications]
                    WHERE [CurrentStatus] NOT IN (N'Draft',N'Pending',N'UnderReview',N'Approved',N'Rejected',N'Withdrawn')
                )
                    THROW 51603, N'Değerlendirme migration ön kontrolü başarısız: tanımsız başvuru durumu bulundu. Veri otomatik değiştirilmedi.', 1;
                """);

            migrationBuilder.AddColumn<DateTime>(
                name: "EvaluationFinalizedAtUtc",
                table: "ProgramOfferings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvaluationState",
                table: "ProgramOfferings",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                defaultValue: "Configuring");

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultsPublishedAtUtc",
                table: "ProgramOfferings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UsesEvaluationWorkflow",
                table: "ProgramOfferings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UsesEvaluationWorkflow",
                table: "Applications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ApplicationEvaluations",
                columns: table => new
                {
                    ApplicationEvaluationID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationID = table.Column<int>(type: "int", nullable: false),
                    ProgramOfferingID = table.Column<int>(type: "int", nullable: false),
                    EligibilityStatus = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    IneligibilityReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TotalScore = table.Column<decimal>(type: "decimal(7,4)", nullable: true),
                    Rank = table.Column<int>(type: "int", nullable: true),
                    Outcome = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    EligibilityDecidedByAdminID = table.Column<int>(type: "int", nullable: true),
                    EligibilityDecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinalizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationEvaluations", x => x.ApplicationEvaluationID);
                    table.CheckConstraint("CK_ApplicationEvaluations_EligibilityStatus", "[EligibilityStatus] IN ('Pending','Eligible','Ineligible')");
                    table.CheckConstraint("CK_ApplicationEvaluations_EligibilityDecision", "([EligibilityStatus] = 'Pending' AND [EligibilityDecidedByAdminID] IS NULL AND [EligibilityDecidedAtUtc] IS NULL) OR ([EligibilityStatus] IN ('Eligible','Ineligible') AND [EligibilityDecidedByAdminID] IS NOT NULL AND [EligibilityDecidedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_ApplicationEvaluations_FinalResult", "([Outcome] IS NULL AND [Rank] IS NULL AND [FinalizedAtUtc] IS NULL) OR ([Outcome] IN ('Admitted','NotAdmitted') AND [Rank] IS NOT NULL AND [TotalScore] IS NOT NULL AND [FinalizedAtUtc] IS NOT NULL) OR ([Outcome] = 'Ineligible' AND [Rank] IS NULL AND [TotalScore] IS NULL AND [FinalizedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_ApplicationEvaluations_IneligibilityReason", "([EligibilityStatus] = 'Ineligible' AND [IneligibilityReason] IS NOT NULL AND LEN(LTRIM(RTRIM([IneligibilityReason]))) > 0) OR ([EligibilityStatus] IN ('Pending','Eligible') AND [IneligibilityReason] IS NULL)");
                    table.CheckConstraint("CK_ApplicationEvaluations_Outcome", "[Outcome] IS NULL OR [Outcome] IN ('Admitted','NotAdmitted','Ineligible')");
                    table.CheckConstraint("CK_ApplicationEvaluations_Rank", "[Rank] IS NULL OR [Rank] > 0");
                    table.CheckConstraint("CK_ApplicationEvaluations_TotalScore", "[TotalScore] IS NULL OR [TotalScore] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_ApplicationEvaluations_Admins_EligibilityDecidedByAdminID",
                        column: x => x.EligibilityDecidedByAdminID,
                        principalTable: "Admins",
                        principalColumn: "AdminID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApplicationEvaluations_Applications_ApplicationID",
                        column: x => x.ApplicationID,
                        principalTable: "Applications",
                        principalColumn: "ApplicationID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApplicationEvaluations_ProgramOfferings_ProgramOfferingID",
                        column: x => x.ProgramOfferingID,
                        principalTable: "ProgramOfferings",
                        principalColumn: "ProgramOfferingID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProgramOfferingEvaluationCriteria",
                columns: table => new
                {
                    CriterionID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicID = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    ProgramOfferingID = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    NormalizedCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SourceType = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ExamID = table.Column<int>(type: "int", nullable: true),
                    WeightBasisPoints = table.Column<int>(type: "int", nullable: false),
                    MaximumRawScore = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    TieBreakPriority = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramOfferingEvaluationCriteria", x => x.CriterionID);
                    table.CheckConstraint("CK_ProgramOfferingEvaluationCriteria_Code", "[Code] = LTRIM(RTRIM([Code])) AND LEN([Code]) BETWEEN 2 AND 64");
                    table.CheckConstraint("CK_ProgramOfferingEvaluationCriteria_SourceConfiguration", "([SourceType] = 'UndergraduateGpa' AND [ExamID] IS NULL AND [MaximumRawScore] = 4) OR ([SourceType] = 'ExamScore' AND [ExamID] IS NOT NULL AND [MaximumRawScore] > 0) OR ([SourceType] = 'ManualScore' AND [ExamID] IS NULL AND [MaximumRawScore] = 100)");
                    table.CheckConstraint("CK_ProgramOfferingEvaluationCriteria_SourceType", "[SourceType] IN ('UndergraduateGpa','ExamScore','ManualScore')");
                    table.CheckConstraint("CK_ProgramOfferingEvaluationCriteria_TieBreak", "[TieBreakPriority] > 0");
                    table.CheckConstraint("CK_ProgramOfferingEvaluationCriteria_Weight", "[WeightBasisPoints] BETWEEN 1 AND 10000");
                    table.ForeignKey(
                        name: "FK_ProgramOfferingEvaluationCriteria_Exams_ExamID",
                        column: x => x.ExamID,
                        principalTable: "Exams",
                        principalColumn: "ExamID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgramOfferingEvaluationCriteria_ProgramOfferings_ProgramOfferingID",
                        column: x => x.ProgramOfferingID,
                        principalTable: "ProgramOfferings",
                        principalColumn: "ProgramOfferingID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationEvaluationComponents",
                columns: table => new
                {
                    ComponentID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationEvaluationID = table.Column<int>(type: "int", nullable: false),
                    SourceCriterionID = table.Column<int>(type: "int", nullable: true),
                    CriterionPublicIDSnapshot = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeSnapshot = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    DisplayNameSnapshot = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SourceTypeSnapshot = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ExamID = table.Column<int>(type: "int", nullable: true),
                    RawScore = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    MaximumRawScoreSnapshot = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    NormalizedScore = table.Column<decimal>(type: "decimal(7,4)", nullable: true),
                    WeightBasisPointsSnapshot = table.Column<int>(type: "int", nullable: false),
                    WeightedScore = table.Column<decimal>(type: "decimal(7,4)", nullable: true),
                    TieBreakPrioritySnapshot = table.Column<int>(type: "int", nullable: false),
                    ManualScoredByAdminID = table.Column<int>(type: "int", nullable: true),
                    ManualScoredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationEvaluationComponents", x => x.ComponentID);
                    table.CheckConstraint("CK_ApplicationEvaluationComponents_NormalizedScore", "[NormalizedScore] IS NULL OR [NormalizedScore] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_ApplicationEvaluationComponents_ManualAudit", "([ManualScoredByAdminID] IS NULL AND [ManualScoredAtUtc] IS NULL) OR ([SourceTypeSnapshot] = 'ManualScore' AND [ManualScoredByAdminID] IS NOT NULL AND [ManualScoredAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_ApplicationEvaluationComponents_RawScore", "[RawScore] IS NULL OR ([RawScore] >= 0 AND [RawScore] <= [MaximumRawScoreSnapshot])");
                    table.CheckConstraint("CK_ApplicationEvaluationComponents_SourceType", "[SourceTypeSnapshot] IN ('UndergraduateGpa','ExamScore','ManualScore')");
                    table.CheckConstraint("CK_ApplicationEvaluationComponents_SourceConfiguration", "([SourceTypeSnapshot] = 'UndergraduateGpa' AND [ExamID] IS NULL AND [MaximumRawScoreSnapshot] = 4) OR ([SourceTypeSnapshot] = 'ExamScore' AND [ExamID] IS NOT NULL AND [MaximumRawScoreSnapshot] > 0) OR ([SourceTypeSnapshot] = 'ManualScore' AND [ExamID] IS NULL AND [MaximumRawScoreSnapshot] = 100)");
                    table.CheckConstraint("CK_ApplicationEvaluationComponents_ScoreCompleteness", "([RawScore] IS NULL AND [NormalizedScore] IS NULL AND [WeightedScore] IS NULL) OR ([RawScore] IS NOT NULL AND [NormalizedScore] IS NOT NULL AND [WeightedScore] IS NOT NULL)");
                    table.CheckConstraint("CK_ApplicationEvaluationComponents_TieBreak", "[TieBreakPrioritySnapshot] > 0");
                    table.CheckConstraint("CK_ApplicationEvaluationComponents_Weight", "[WeightBasisPointsSnapshot] BETWEEN 1 AND 10000");
                    table.CheckConstraint("CK_ApplicationEvaluationComponents_WeightedScore", "[WeightedScore] IS NULL OR [WeightedScore] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_ApplicationEvaluationComponents_Admins_ManualScoredByAdminID",
                        column: x => x.ManualScoredByAdminID,
                        principalTable: "Admins",
                        principalColumn: "AdminID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApplicationEvaluationComponents_ApplicationEvaluations_ApplicationEvaluationID",
                        column: x => x.ApplicationEvaluationID,
                        principalTable: "ApplicationEvaluations",
                        principalColumn: "ApplicationEvaluationID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApplicationEvaluationComponents_ProgramOfferingEvaluationCriteria_SourceCriterionID",
                        column: x => x.SourceCriterionID,
                        principalTable: "ProgramOfferingEvaluationCriteria",
                        principalColumn: "CriterionID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProgramOfferings_EvaluationState",
                table: "ProgramOfferings",
                sql: "[EvaluationState] IN ('Configuring','Finalized','Published')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProgramOfferings_EvaluationLifecycle",
                table: "ProgramOfferings",
                sql: "([EvaluationState] = 'Configuring' AND [EvaluationFinalizedAtUtc] IS NULL AND [ResultsPublishedAtUtc] IS NULL) OR ([EvaluationState] = 'Finalized' AND [EvaluationFinalizedAtUtc] IS NOT NULL AND [ResultsPublishedAtUtc] IS NULL) OR ([EvaluationState] = 'Published' AND [EvaluationFinalizedAtUtc] IS NOT NULL AND [ResultsPublishedAtUtc] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvaluationComponents_ApplicationEvaluationID_CodeSnapshot",
                table: "ApplicationEvaluationComponents",
                columns: new[] { "ApplicationEvaluationID", "CodeSnapshot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvaluationComponents_ApplicationEvaluationID_TieBreakPrioritySnapshot",
                table: "ApplicationEvaluationComponents",
                columns: new[] { "ApplicationEvaluationID", "TieBreakPrioritySnapshot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvaluationComponents_ManualScoredByAdminID",
                table: "ApplicationEvaluationComponents",
                column: "ManualScoredByAdminID");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvaluationComponents_SourceCriterionID",
                table: "ApplicationEvaluationComponents",
                column: "SourceCriterionID");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvaluations_ApplicationID",
                table: "ApplicationEvaluations",
                column: "ApplicationID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvaluations_EligibilityDecidedByAdminID",
                table: "ApplicationEvaluations",
                column: "EligibilityDecidedByAdminID");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvaluations_ProgramOfferingID_Rank",
                table: "ApplicationEvaluations",
                columns: new[] { "ProgramOfferingID", "Rank" },
                unique: true,
                filter: "[Rank] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProgramOfferingEvaluationCriteria_ExamID",
                table: "ProgramOfferingEvaluationCriteria",
                column: "ExamID");

            migrationBuilder.CreateIndex(
                name: "IX_ProgramOfferingEvaluationCriteria_ProgramOfferingID_ExamID",
                table: "ProgramOfferingEvaluationCriteria",
                columns: new[] { "ProgramOfferingID", "ExamID" },
                unique: true,
                filter: "[ExamID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProgramOfferingEvaluationCriteria_ProgramOfferingID_NormalizedCode",
                table: "ProgramOfferingEvaluationCriteria",
                columns: new[] { "ProgramOfferingID", "NormalizedCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgramOfferingEvaluationCriteria_ProgramOfferingID_SourceType",
                table: "ProgramOfferingEvaluationCriteria",
                columns: new[] { "ProgramOfferingID", "SourceType" },
                unique: true,
                filter: "[SourceType] = 'UndergraduateGpa'");

            migrationBuilder.CreateIndex(
                name: "IX_ProgramOfferingEvaluationCriteria_ProgramOfferingID_TieBreakPriority",
                table: "ProgramOfferingEvaluationCriteria",
                columns: new[] { "ProgramOfferingID", "TieBreakPriority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgramOfferingEvaluationCriteria_PublicID",
                table: "ProgramOfferingEvaluationCriteria",
                column: "PublicID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "AddApplicationEvaluationAndResults migration geri alınamaz; kriter snapshot'ları, sıralama ve yayımlanmış sonuçları kaldırmak veri kaybına yol açabilir.");
        }
    }
}
