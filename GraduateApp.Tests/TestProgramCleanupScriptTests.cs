namespace GraduateApp.Tests;

public sealed class TestProgramCleanupScriptTests
{
    [Fact]
    public void Cleanup_script_is_localdb_guarded_transactional_and_exactly_scoped()
    {
        var script = ReadScript();

        Assert.Contains("SERVERPROPERTY('IsLocalDB')", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DB_NAME()", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRANSACTION", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ROLLBACK TRANSACTION", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET XACT_ABORT ON", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("N'smoke2', N'smokeprogram', N'smokes', N'UAT RC1 Program'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgramName LIKE", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ApplicationDocuments", script, StringComparison.Ordinal);
        Assert.Contains("ReferenceLetters", script, StringComparison.Ordinal);
        Assert.Contains("storage temizliği", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sys.foreign_keys", script, StringComparison.OrdinalIgnoreCase);
    }

    [LocalDbFact]
    public async Task Cleanup_script_removes_only_exact_test_program_subtrees()
    {
        var databaseName = $"GraduateAppCleanupScript_{Guid.NewGuid():N}";
        await using var database = new LocalDbTestDatabase(databaseName, null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        await database.ExecuteAsync(SeedSql);

        var script = $"EXEC sys.sp_set_session_context @key=N'GraduateApp.ExpectedDatabase', @value=N'{databaseName}';{Environment.NewLine}{ReadScript()}";
        await database.ExecuteSqlServerScriptAsync(script);
        await database.ExecuteSqlServerScriptAsync(script);

        Assert.Equal(
            0,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.Programs WHERE ProgramName IN (N'smoke2', N'smokeprogram', N'smokes', N'UAT RC1 Program');"));
        Assert.Equal(
            2,
            await database.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Programs;"));
        Assert.Equal(
            1,
            await database.ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.Programs WHERE ProgramName = N'smoke2 archival';"));
        Assert.Equal(
            2,
            await database.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProgramOfferings;"));
        Assert.Equal(
            2,
            await database.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProgramOfferingDocumentRequirements;"));
        Assert.Equal(
            2,
            await database.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ProgramOfferingEvaluationCriteria;"));
    }

    private static string ReadScript()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "Cleanup-LocalTestPrograms.sql"));
    }

    private const string SeedSql = """
        INSERT INTO dbo.Institutes (InstituteName)
        VALUES (N'Test Enstitüsü');
        DECLARE @InstituteID int = SCOPE_IDENTITY();

        INSERT INTO dbo.Programs (InstituteID, ProgramName, DegreeType)
        VALUES
            (@InstituteID, N'smoke2', N'Doktora'),
            (@InstituteID, N'smoke2 archival', N'Doktora'),
            (@InstituteID, N'Production Program', N'Doktora');

        DECLARE @SmokeProgramID int = (SELECT ProgramID FROM dbo.Programs WHERE ProgramName = N'smoke2');
        DECLARE @ArchiveProgramID int = (SELECT ProgramID FROM dbo.Programs WHERE ProgramName = N'smoke2 archival');
        DECLARE @ProductionProgramID int = (SELECT ProgramID FROM dbo.Programs WHERE ProgramName = N'Production Program');

        INSERT INTO dbo.ProgramOfferings
            (ProgramID, AcademicYearStart, Term, Quota, IsOpen, IsArchived)
        VALUES
            (@SmokeProgramID, 2026, 1, 1, 0, 0),
            (@ArchiveProgramID, 2026, 1, 1, 0, 0),
            (@ProductionProgramID, 2026, 1, 1, 0, 0);

        INSERT INTO dbo.ProgramOfferingDocumentRequirements
            (ProgramOfferingID, DocumentCode, NormalizedDocumentCode, DisplayName,
             IsRequired, IsActive, AllowedContentCategory, MaximumBytes)
        SELECT ProgramOfferingID, N'transcript', N'TRANSCRIPT', N'Transcript',
               1, 1, N'PdfOnly', 1048576
        FROM dbo.ProgramOfferings;

        INSERT INTO dbo.ProgramOfferingEvaluationCriteria
            (ProgramOfferingID, Code, NormalizedCode, DisplayName, SourceType,
             WeightBasisPoints, MaximumRawScore, TieBreakPriority)
        SELECT ProgramOfferingID, N'gpa', N'GPA', N'GPA', N'UndergraduateGpa',
               10000, 4, 1
        FROM dbo.ProgramOfferings;
        """;
}
