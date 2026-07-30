using Microsoft.Data.SqlClient;

namespace GraduateApp.Tests;

public sealed class LocalDbConnectionRetryTests
{
    [Fact]
    public async Task First_transient_open_failure_is_disposed_before_a_new_connection_succeeds()
    {
        var connections = new List<FakeConnection>();
        var delays = new List<TimeSpan>();
        var openAttempts = 0;

        var result = await LocalDbTestSupport.OpenConnectionWithRetryAsync(
            () =>
            {
                var connection = new FakeConnection();
                connections.Add(connection);
                return connection;
            },
            (_, _) => ++openAttempts == 1
                ? Task.FromException(TestSqlExceptionFactory.Create(-2))
                : Task.CompletedTask,
            connection => connection.DisposeAsync(),
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(2, openAttempts);
        Assert.Equal(2, connections.Count);
        Assert.True(connections[0].IsDisposed);
        Assert.False(connections[1].IsDisposed);
        Assert.Same(connections[1], result);
        Assert.Equal([TimeSpan.FromMilliseconds(100)], delays);
        await result.DisposeAsync();
    }

    [Fact]
    public async Task Four_transient_open_failures_use_bounded_backoff_before_fifth_attempt_succeeds()
    {
        var connections = new List<FakeConnection>();
        var delays = new List<TimeSpan>();
        var openAttempts = 0;

        var result = await LocalDbTestSupport.OpenConnectionWithRetryAsync(
            () =>
            {
                var connection = new FakeConnection();
                connections.Add(connection);
                return connection;
            },
            (_, _) => ++openAttempts < LocalDbTestSupport.ConnectionOpenMaxAttempts
                ? Task.FromException(TestSqlExceptionFactory.Create(-2))
                : Task.CompletedTask,
            connection => connection.DisposeAsync(),
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(LocalDbTestSupport.ConnectionOpenMaxAttempts, openAttempts);
        Assert.Equal(LocalDbTestSupport.ConnectionOpenMaxAttempts, connections.Count);
        Assert.All(connections[..^1], connection => Assert.True(connection.IsDisposed));
        Assert.False(connections[^1].IsDisposed);
        Assert.Same(connections[^1], result);
        Assert.Equal(
            [
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(1000)
            ],
            delays);
        await result.DisposeAsync();
    }

    [Fact]
    public async Task Transient_open_failure_is_propagated_after_the_bounded_attempt_limit()
    {
        var connections = new List<FakeConnection>();
        var delayCount = 0;
        var openAttempts = 0;

        var exception = await Assert.ThrowsAsync<SqlException>(() =>
            LocalDbTestSupport.OpenConnectionWithRetryAsync(
                () =>
                {
                    var connection = new FakeConnection();
                    connections.Add(connection);
                    return connection;
                },
                (_, _) =>
                {
                    openAttempts++;
                    return Task.FromException(TestSqlExceptionFactory.Create(-2));
                },
                connection => connection.DisposeAsync(),
                (_, _) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                },
                CancellationToken.None));

        Assert.Equal(-2, exception.Number);
        Assert.Equal(LocalDbTestSupport.ConnectionOpenMaxAttempts, openAttempts);
        Assert.Equal(LocalDbTestSupport.ConnectionOpenMaxAttempts, connections.Count);
        Assert.Equal(LocalDbTestSupport.ConnectionOpenMaxAttempts - 1, delayCount);
        Assert.All(connections, connection => Assert.True(connection.IsDisposed));
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_before_a_connection_is_created()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var connectionCount = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LocalDbTestSupport.OpenConnectionWithRetryAsync(
                () =>
                {
                    connectionCount++;
                    return new FakeConnection();
                },
                (_, _) => Task.CompletedTask,
                connection => connection.DisposeAsync(),
                (_, _) => Task.CompletedTask,
                cancellation.Token));

        Assert.Equal(0, connectionCount);
    }

    [Theory]
    [InlineData(53)]
    [InlineData(4060)]
    [InlineData(18456)]
    public async Task Configuration_and_authentication_errors_are_not_retried(int errorNumber)
    {
        var connection = new FakeConnection();
        var openAttempts = 0;
        var delayCount = 0;

        var exception = await Assert.ThrowsAsync<SqlException>(() =>
            LocalDbTestSupport.OpenConnectionWithRetryAsync(
                () => connection,
                (_, _) =>
                {
                    openAttempts++;
                    return Task.FromException(TestSqlExceptionFactory.Create(errorNumber));
                },
                candidate => candidate.DisposeAsync(),
                (_, _) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                },
                CancellationToken.None));

        Assert.Equal(errorNumber, exception.Number);
        Assert.Equal(1, openAttempts);
        Assert.Equal(0, delayCount);
        Assert.True(connection.IsDisposed);
    }

    [Fact]
    public async Task Fixture_database_command_is_outside_the_open_retry_boundary_and_sent_once()
    {
        const string commandText = "CREATE TABLE [dbo].[Probe] ([Id] int NOT NULL);";
        var connections = new List<SqlConnection>();
        var openAttempts = 0;
        var delayCount = 0;
        var commandCount = 0;

        Task<SqlConnection> OpenDatabaseAsync(
            string _,
            CancellationToken cancellationToken) =>
            LocalDbTestSupport.OpenConnectionWithRetryAsync(
                () =>
                {
                    var connection = new SqlConnection();
                    connections.Add(connection);
                    return connection;
                },
                (_, _) => ++openAttempts == 1
                    ? Task.FromException(TestSqlExceptionFactory.Create(-2))
                    : Task.CompletedTask,
                static connection => connection.DisposeAsync(),
                (_, _) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                },
                cancellationToken);

        await using var database = new LocalDbTestDatabase(
            $"GraduateAppFixtureRetry_{Guid.NewGuid():N}",
            databaseCollation: null,
            static _ => Task.FromResult(new SqlConnection()),
            static (_, _, _) => Task.CompletedTask,
            OpenDatabaseAsync,
            (_, actualCommandText, _) =>
            {
                commandCount++;
                Assert.Equal(commandText, actualCommandText);
                return Task.FromResult(1);
            });

        var affectedRows = await database.ExecuteAsync(commandText);

        Assert.Equal(1, affectedRows);
        Assert.Equal(2, openAttempts);
        Assert.Equal(2, connections.Count);
        Assert.Equal(1, delayCount);
        Assert.Equal(1, commandCount);
    }

    [Fact]
    public async Task Create_database_command_is_outside_the_open_retry_boundary_and_sent_once()
    {
        var openAttempts = 0;
        var commands = new List<string>();
        Task<SqlConnection> OpenMasterAsync(CancellationToken cancellationToken) =>
            LocalDbTestSupport.OpenConnectionWithRetryAsync(
                static () => new SqlConnection(),
                (_, _) => ++openAttempts == 1
                    ? Task.FromException(TestSqlExceptionFactory.Create(-2))
                    : Task.CompletedTask,
                static connection => connection.DisposeAsync(),
                static (_, _) => Task.CompletedTask,
                cancellationToken);
        var database = new LocalDbTestDatabase(
            $"GraduateAppRetryBoundary_{Guid.NewGuid():N}",
            databaseCollation: null,
            OpenMasterAsync,
            (_, commandText, _) =>
            {
                commands.Add(commandText);
                return Task.CompletedTask;
            });

        await database.CreateAsync();

        Assert.Equal(2, openAttempts);
        Assert.Single(commands, command => command.StartsWith("CREATE DATABASE", StringComparison.Ordinal));
        await database.DisposeAsync();
        Assert.Single(commands, command => command.StartsWith("CREATE DATABASE", StringComparison.Ordinal));
        Assert.Single(commands, command => command.Contains("DROP DATABASE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exhausted_open_retries_send_no_database_command_and_leave_no_live_attempt()
    {
        var connections = new List<SqlConnection>();
        var commandCount = 0;
        Task<SqlConnection> OpenMasterAsync(CancellationToken cancellationToken) =>
            LocalDbTestSupport.OpenConnectionWithRetryAsync(
                () =>
                {
                    var connection = new SqlConnection();
                    connections.Add(connection);
                    return connection;
                },
                (_, _) => Task.FromException(TestSqlExceptionFactory.Create(-2)),
                static connection => connection.DisposeAsync(),
                static (_, _) => Task.CompletedTask,
                cancellationToken);
        var database = new LocalDbTestDatabase(
            $"GraduateAppRetryFailure_{Guid.NewGuid():N}",
            databaseCollation: null,
            OpenMasterAsync,
            (_, _, _) =>
            {
                commandCount++;
                return Task.CompletedTask;
            });

        await Assert.ThrowsAsync<SqlException>(() => database.CreateAsync());
        await database.DisposeAsync();

        Assert.Equal(0, commandCount);
        Assert.Equal(LocalDbTestSupport.ConnectionOpenMaxAttempts, connections.Count);
        Assert.All(connections, connection => Assert.Equal(System.Data.ConnectionState.Closed, connection.State));
    }

    private sealed class FakeConnection : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
