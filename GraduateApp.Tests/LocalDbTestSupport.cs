using GraduateApp.API.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;

namespace GraduateApp.Tests;

internal sealed class LocalDbFactAttribute : FactAttribute
{
    public LocalDbFactAttribute()
    {
        if (!LocalDbTestSupport.IsAvailable)
        {
            Skip = LocalDbTestSupport.UnavailableReason;
        }
    }
}

internal sealed class LocalDbTheoryAttribute : TheoryAttribute
{
    public LocalDbTheoryAttribute()
    {
        if (!LocalDbTestSupport.IsAvailable)
        {
            Skip = LocalDbTestSupport.UnavailableReason;
        }
    }
}

internal static class LocalDbTestSupport
{
    private static readonly Lazy<(bool IsAvailable, string? Reason)> Availability = new(ProbeAvailability);

    public static bool IsAvailable => Availability.Value.IsAvailable;
    public static string UnavailableReason => Availability.Value.Reason ?? "SQL Server LocalDB kullanılamıyor.";

    public static async Task<LocalDbTestDatabase> CreateDatabaseAsync(
        string studentsSchemaSql,
        string? databaseCollation = null)
    {
        var database = new LocalDbTestDatabase(
            $"GraduateAppTelephone_{Guid.NewGuid():N}",
            databaseCollation);
        await database.CreateAsync();
        try
        {
            await database.ExecuteAsync(studentsSchemaSql);
            await database.ExecuteAsync(
                """
                CREATE TABLE [dbo].[__EFMigrationsHistory]
                (
                    [MigrationId] nvarchar(150) NOT NULL,
                    [ProductVersion] nvarchar(32) NOT NULL,
                    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                );
                INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES (N'20260717065942_HardenExistingSchema', N'10.0.10');
                """);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public static async Task<SqlException> CreateUniqueViolationExceptionAsync(int errorNumber)
    {
        await using var connection = new SqlConnection(CreateConnectionString("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = errorNumber switch
        {
            2601 =>
                """
                CREATE TABLE #DuplicateIndex ([Value] int NOT NULL);
                CREATE UNIQUE INDEX [UX_DuplicateIndex_Value] ON #DuplicateIndex ([Value]);
                INSERT INTO #DuplicateIndex ([Value]) VALUES (1), (1);
                """,
            2627 =>
                """
                CREATE TABLE #DuplicateConstraint
                (
                    [Value] int NOT NULL,
                    CONSTRAINT [UQ_DuplicateConstraint_Value] UNIQUE ([Value])
                );
                INSERT INTO #DuplicateConstraint ([Value]) VALUES (1), (1);
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(errorNumber))
        };

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException exception) when (exception.Number == errorNumber)
        {
            return exception;
        }

        throw new InvalidOperationException($"SQL Server {errorNumber} hatası üretilemedi.");
    }

    internal static string CreateConnectionString(string databaseName) =>
        new SqlConnectionStringBuilder
        {
            DataSource = "(localdb)\\MSSQLLocalDB",
            InitialCatalog = databaseName,
            IntegratedSecurity = true,
            Encrypt = false,
            ConnectTimeout = 5,
            MultipleActiveResultSets = false
        }.ConnectionString;

    internal static string QuoteIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    internal static string GetDatabaseCollationClause(string? databaseCollation) => databaseCollation switch
    {
        null => string.Empty,
        "Turkish_100_CI_AS" => " COLLATE Turkish_100_CI_AS",
        _ => throw new ArgumentOutOfRangeException(
            nameof(databaseCollation),
            "Test veritabanı için izin verilmeyen collation istendi.")
    };

    private static (bool IsAvailable, string? Reason) ProbeAvailability()
    {
        try
        {
            using var connection = new SqlConnection(CreateConnectionString("master"));
            connection.Open();
            return (true, null);
        }
        catch (Exception exception)
        {
            return (false, $"SQL Server LocalDB kullanılamıyor ({exception.GetType().Name}).");
        }
    }
}

internal sealed class LocalDbTestDatabase(string databaseName, string? databaseCollation) : IAsyncDisposable
{
    public string ConnectionString => LocalDbTestSupport.CreateConnectionString(databaseName);

    public async Task CreateAsync()
    {
        await using var connection = new SqlConnection(LocalDbTestSupport.CreateConnectionString("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {LocalDbTestSupport.QuoteIdentifier(databaseName)}{LocalDbTestSupport.GetDatabaseCollationClause(databaseCollation)};";
        await command.ExecuteNonQueryAsync();
    }

    public async Task MigrateAsync(string? targetMigration = null)
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using var dbContext = new GraduateAppDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration);
    }

    public async Task CreateCurrentModelSchemaAsync()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using var dbContext = new GraduateAppDbContext(options);
        var created = await dbContext.Database.EnsureCreatedAsync();
        if (!created)
        {
            _ = await dbContext.ProgramOfferings.AsNoTracking().AnyAsync();
        }
    }

    public async Task<int> ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }

    public async Task ExecuteSqlServerScriptAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(
            sql,
            @"^\s*GO\s*(?:--.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull)
        {
            throw new InvalidOperationException("Test SQL sorgusu beklenen scalar değeri döndürmedi.");
        }

        return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(LocalDbTestSupport.CreateConnectionString("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var quotedDatabaseName = LocalDbTestSupport.QuoteIdentifier(databaseName);
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE {quotedDatabaseName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE {quotedDatabaseName};
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }
}
