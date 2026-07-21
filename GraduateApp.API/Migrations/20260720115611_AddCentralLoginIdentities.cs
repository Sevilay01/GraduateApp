using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GraduateApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCentralLoginIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[Students] AS student
                    INNER JOIN [dbo].[Admins] AS admin
                        ON admin.[NormalizedEmail] = student.[NormalizedEmail]
                )
                BEGIN
                    THROW 51020, 'Merkezi giriş kimliği oluşturulamadı: öğrenci ve yönetici hesapları arasında e-posta çakışması var. Migration hiçbir veri değiştirmeden durduruldu; çakışmayı yetkili bir işlemle çözün.', 1;
                END;
                """);

            migrationBuilder.CreateTable(
                name: "LoginIdentities",
                columns: table => new
                {
                    LoginIdentityID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    AccountType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StudentTC = table.Column<string>(type: "char(11)", unicode: false, fixedLength: true, maxLength: 11, nullable: true),
                    AdminID = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginIdentities", x => x.LoginIdentityID);
                    table.CheckConstraint("CK_LoginIdentities_Subject", "([AccountType] = N'Student' AND [StudentTC] IS NOT NULL AND [AdminID] IS NULL) OR ([AccountType] = N'Admin' AND [StudentTC] IS NULL AND [AdminID] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_LoginIdentities_Admins_AdminID",
                        column: x => x.AdminID,
                        principalTable: "Admins",
                        principalColumn: "AdminID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoginIdentities_Students_StudentTC",
                        column: x => x.StudentTC,
                        principalTable: "Students",
                        principalColumn: "TC",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoginIdentities_AdminID",
                table: "LoginIdentities",
                column: "AdminID",
                unique: true,
                filter: "[AdminID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LoginIdentities_NormalizedEmail",
                table: "LoginIdentities",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoginIdentities_StudentTC",
                table: "LoginIdentities",
                column: "StudentTC",
                unique: true,
                filter: "[StudentTC] IS NOT NULL");

            migrationBuilder.Sql(
                """
                INSERT INTO [dbo].[LoginIdentities]
                    ([NormalizedEmail], [AccountType], [StudentTC], [AdminID], [CreatedAtUtc])
                SELECT
                    student.[NormalizedEmail], N'Student', student.[TC], NULL,
                    COALESCE(student.[CreatedAtUtc], SYSUTCDATETIME())
                FROM [dbo].[Students] AS student;

                INSERT INTO [dbo].[LoginIdentities]
                    ([NormalizedEmail], [AccountType], [StudentTC], [AdminID], [CreatedAtUtc])
                SELECT
                    admin.[NormalizedEmail], N'Admin', NULL, admin.[AdminID],
                    COALESCE(admin.[CreatedAtUtc], SYSUTCDATETIME())
                FROM [dbo].[Admins] AS admin;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                THROW 51021, 'LoginIdentities geri alınamaz: merkezi kimlik kayıtlarını silmek giriş bütünlüğünü bozabilir. Geri dönüş için incelenmiş bir ileri migration kullanın.', 1;
                """);
        }
    }
}
