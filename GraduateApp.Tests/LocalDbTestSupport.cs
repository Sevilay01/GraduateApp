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
    internal const int ConnectionOpenMaxAttempts = 3;
    private static readonly TimeSpan[] ConnectionOpenRetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250)
    ];
    private static readonly HashSet<int> RetryableConnectionOpenErrorNumbers =
    [
        -2,
        64,
        121,
        233,
        258,
        10053,
        10054,
        10060
    ];
    private static readonly Lazy<(bool IsAvailable, string? Reason)> Availability = new(ProbeAvailability);
    private static readonly Lazy<Task<SqlException>> DeadlockException = new(CreateDeadlockExceptionCoreAsync);

    public static bool IsAvailable => Availability.Value.IsAvailable;
    public static string UnavailableReason => Availability.Value.Reason ?? "SQL Server LocalDB kullanılamıyor.";

    public static async Task<LocalDbTestDatabase> CreateDatabaseAsync(
        string studentsSchemaSql,
        string? databaseCollation = null)
    {
        var database = new LocalDbTestDatabase(
            $"GraduateAppTelephone_{Guid.NewGuid():N}",
            databaseCollation);
        try
        {
            await database.CreateAsync();
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

    public static Task<SqlException> CreateDeadlockExceptionAsync() => DeadlockException.Value;

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

    internal static Task<SqlConnection> OpenMasterConnectionAsync(
        CancellationToken cancellationToken = default) =>
        OpenDatabaseConnectionAsync(CreateConnectionString("master"), cancellationToken);

    internal static Task<SqlConnection> OpenDatabaseConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken = default) =>
        OpenConnectionWithRetryAsync(
            () => new SqlConnection(connectionString),
            static (connection, token) => connection.OpenAsync(token),
            static connection => connection.DisposeAsync(),
            static (delay, token) => Task.Delay(delay, token),
            cancellationToken);

    internal static async Task<TConnection> OpenConnectionWithRetryAsync<TConnection>(
        Func<TConnection> connectionFactory,
        Func<TConnection, CancellationToken, Task> openAsync,
        Func<TConnection, ValueTask> disposeAsync,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            var connection = connectionFactory();
            try
            {
                await openAsync(connection, cancellationToken);
                return connection;
            }
            catch (SqlException exception) when (
                attempt < ConnectionOpenMaxAttempts
                && IsRetryableConnectionOpenFailure(exception))
            {
                await disposeAsync(connection);
                await delayAsync(ConnectionOpenRetryDelays[attempt - 1], cancellationToken);
            }
            catch
            {
                await disposeAsync(connection);
                throw;
            }
        }
    }

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

    private static bool IsRetryableConnectionOpenFailure(SqlException exception) =>
        RetryableConnectionOpenErrorNumbers.Contains(exception.Number)
        || exception.Errors.Cast<SqlError>().Any(
            error => RetryableConnectionOpenErrorNumbers.Contains(error.Number));

    private static async Task<SqlException> CreateDeadlockExceptionCoreAsync()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppDeadlockException_{Guid.NewGuid():N}",
            null);
        await database.CreateAsync();
        await database.ExecuteAsync(
            """
            CREATE TABLE [dbo].[DeadlockRows]
            (
                [Id] int NOT NULL CONSTRAINT [PK_DeadlockRows] PRIMARY KEY,
                [Value] int NOT NULL
            );
            INSERT INTO [dbo].[DeadlockRows] ([Id], [Value]) VALUES (1, 0), (2, 0);
            """);

        await using var firstConnection = new SqlConnection(database.ConnectionString);
        await using var secondConnection = new SqlConnection(database.ConnectionString);
        await firstConnection.OpenAsync();
        await secondConnection.OpenAsync();
        await using var firstTransaction = firstConnection.BeginTransaction(System.Data.IsolationLevel.Serializable);
        await using var secondTransaction = secondConnection.BeginTransaction(System.Data.IsolationLevel.Serializable);

        await UpdateDeadlockRowAsync(firstConnection, firstTransaction, 1);
        await UpdateDeadlockRowAsync(secondConnection, secondTransaction, 2);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstAttempt = CaptureDeadlockAsync(
            firstConnection,
            firstTransaction,
            rowId: 2,
            start.Task);
        var secondAttempt = CaptureDeadlockAsync(
            secondConnection,
            secondTransaction,
            rowId: 1,
            start.Task);
        start.SetResult();

        var exceptions = (await Task.WhenAll(firstAttempt, secondAttempt))
            .Where(exception => exception is not null)
            .Cast<SqlException>()
            .ToArray();
        var deadlock = Assert.Single(exceptions);
        Assert.Equal(1205, deadlock.Number);
        return deadlock;
    }

    private static async Task<SqlException?> CaptureDeadlockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int rowId,
        Task start)
    {
        await start;
        try
        {
            await UpdateDeadlockRowAsync(connection, transaction, rowId);
            return null;
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            return exception;
        }
    }

    private static async Task UpdateDeadlockRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int rowId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 10;
        command.CommandText = "UPDATE [dbo].[DeadlockRows] SET [Value] = [Value] + 1 WHERE [Id] = @Id;";
        command.Parameters.AddWithValue("@Id", rowId);
        await command.ExecuteNonQueryAsync();
    }
}

internal sealed class LocalDbTestDatabase : IAsyncDisposable
{
    private readonly string databaseName;
    private readonly string? databaseCollation;
    private readonly Func<CancellationToken, Task<SqlConnection>> openMasterConnectionAsync;
    private readonly Func<SqlConnection, string, CancellationToken, Task> executeMasterCommandAsync;
    private readonly Func<string, CancellationToken, Task<SqlConnection>> openDatabaseConnectionAsync;
    private readonly Func<SqlConnection, string, CancellationToken, Task<int>> executeDatabaseCommandAsync;
    private bool createCommandAttempted;

    public LocalDbTestDatabase(string databaseName, string? databaseCollation)
        : this(
            databaseName,
            databaseCollation,
            LocalDbTestSupport.OpenMasterConnectionAsync,
            ExecuteMasterCommandAsync)
    {
    }

    internal LocalDbTestDatabase(
        string databaseName,
        string? databaseCollation,
        Func<CancellationToken, Task<SqlConnection>> openMasterConnectionAsync,
        Func<SqlConnection, string, CancellationToken, Task> executeMasterCommandAsync)
        : this(
            databaseName,
            databaseCollation,
            openMasterConnectionAsync,
            executeMasterCommandAsync,
            LocalDbTestSupport.OpenDatabaseConnectionAsync,
            ExecuteDatabaseCommandAsync)
    {
    }

    internal LocalDbTestDatabase(
        string databaseName,
        string? databaseCollation,
        Func<CancellationToken, Task<SqlConnection>> openMasterConnectionAsync,
        Func<SqlConnection, string, CancellationToken, Task> executeMasterCommandAsync,
        Func<string, CancellationToken, Task<SqlConnection>> openDatabaseConnectionAsync,
        Func<SqlConnection, string, CancellationToken, Task<int>> executeDatabaseCommandAsync)
    {
        this.databaseName = databaseName;
        this.databaseCollation = databaseCollation;
        this.openMasterConnectionAsync = openMasterConnectionAsync;
        this.executeMasterCommandAsync = executeMasterCommandAsync;
        this.openDatabaseConnectionAsync = openDatabaseConnectionAsync;
        this.executeDatabaseCommandAsync = executeDatabaseCommandAsync;
    }

    public string ConnectionString => LocalDbTestSupport.CreateConnectionString(databaseName);

    public async Task CreateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await openMasterConnectionAsync(cancellationToken);
        var commandText =
            $"CREATE DATABASE {LocalDbTestSupport.QuoteIdentifier(databaseName)}{LocalDbTestSupport.GetDatabaseCollationClause(databaseCollation)};";
        createCommandAttempted = true;
        await executeMasterCommandAsync(connection, commandText, cancellationToken);
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
        await using var connection = await openDatabaseConnectionAsync(
            ConnectionString,
            CancellationToken.None);
        return await executeDatabaseCommandAsync(
            connection,
            sql,
            CancellationToken.None);
    }

    public async Task ExecuteSqlServerScriptAsync(string sql)
    {
        await using var connection = await LocalDbTestSupport.OpenDatabaseConnectionAsync(
            ConnectionString,
            CancellationToken.None);
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
        await using var connection = await LocalDbTestSupport.OpenDatabaseConnectionAsync(
            ConnectionString,
            CancellationToken.None);
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
        if (!createCommandAttempted)
        {
            return;
        }

        SqlConnection.ClearAllPools();
        await using var connection = await openMasterConnectionAsync(CancellationToken.None);
        var quotedDatabaseName = LocalDbTestSupport.QuoteIdentifier(databaseName);
        var commandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE {quotedDatabaseName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE {quotedDatabaseName};
            END;
            """;
        await executeMasterCommandAsync(connection, commandText, CancellationToken.None);
        createCommandAttempted = false;
    }

    private static async Task ExecuteMasterCommandAsync(
        SqlConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ExecuteDatabaseCommandAsync(
        SqlConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
