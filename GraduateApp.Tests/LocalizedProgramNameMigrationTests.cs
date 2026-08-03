using ApiProgram = GraduateApp.API.Models.Program;
using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GraduateApp.Tests;

public sealed class LocalizedProgramNameMigrationTests
{
    private const string PreviousMigration = "20260722170322_AddApplicationEvaluationAndResults";
    private const string CurrentMigration = "20260802150000_AddLocalizedProgramName";

    [Fact]
    public void Migration_adds_nullable_bounded_column_and_trim_constraint_without_rewriting_rows()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppLocalizedProgramScriptTest;Integrated Security=true")
            .Options;
        using var db = new GraduateAppDbContext(options);
        var migrator = db.GetService<IMigrator>();

        var script = migrator.GenerateScript(PreviousMigration, CurrentMigration);

        Assert.Contains("ADD [ProgramNameEnglish] nvarchar(100) NULL", script, StringComparison.Ordinal);
        Assert.Contains("CK_Programs_ProgramNameEnglish_Trimmed", script, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE [Programs]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP COLUMN", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runtime_model_exposes_optional_english_program_name_with_expected_limit()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=GraduateAppLocalizedProgramModelTest;Integrated Security=true")
            .Options;
        using var db = new GraduateAppDbContext(options);

        var entity = db.Model.FindEntityType(typeof(ApiProgram));
        Assert.NotNull(entity);
        var property = entity!.FindProperty(nameof(ApiProgram.ProgramNameEnglish));
        Assert.NotNull(property);

        Assert.True(property!.IsNullable);
        Assert.Equal(100, property.GetMaxLength());
    }
}
