using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GraduateApp.API.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyApplicationStatusTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[trg_UpdateApplicationStatus]', N'TR') IS NOT NULL
                    DROP TRIGGER [dbo].[trg_UpdateApplicationStatus];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                THROW 52020, N'RemoveLegacyApplicationStatusTrigger migration geri alınamaz. Bilinmeyen legacy trigger tanımı güvenli biçimde yeniden oluşturulamaz; doğrulanmış DBA betiği ve manuel inceleme gerekir.', 1;
                """);
        }
    }
}
