using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GraduateApp.API.Migrations;

[DbContext(typeof(GraduateAppDbContext))]
[Migration("20260802150000_AddLocalizedProgramName")]
public partial class AddLocalizedProgramName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProgramNameEnglish",
            table: "Programs",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Programs_ProgramNameEnglish_Trimmed",
            table: "Programs",
            sql: "[ProgramNameEnglish] IS NULL OR ([ProgramNameEnglish] = LTRIM(RTRIM([ProgramNameEnglish])) AND LEN([ProgramNameEnglish]) >= 2)");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "İngilizce program adı verisi kaybolabileceği için bu migration otomatik olarak geri alınamaz.");
}
