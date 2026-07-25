using System.Security.Claims;
using GraduateApp.API.Models;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace GraduateApp.Tests;

internal static class TestDb
{
    public static GraduateAppDbContext Create(params IInterceptor[] interceptors)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"));
        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        var options = optionsBuilder.Options;
        return new GraduateAppDbContext(options);
    }

    public static GraduateAppDbContext CreateWithStrictQueryWarnings()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Throw(
                CoreEventId.RowLimitingOperationWithoutOrderByWarning))
            .Options;
        return new GraduateAppDbContext(options);
    }
}

internal sealed class ThrowingSaveChangesInterceptor(Func<Exception> exceptionFactory) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<InterceptionResult<int>>(exceptionFactory());
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(formatter(state, exception));
        if (exception is not null)
        {
            Entries.Add(exception.ToString());
        }
    }
}

internal sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; private set; } = utcNow;
    public override DateTimeOffset GetUtcNow() => UtcNow;
    public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
}

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "GraduateApp.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "GraduateApp.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public DirectoryInfo DirectoryInfo => new(Path);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal static class TestSqlExceptionFactory
{
    public static SqlException Create(int number)
    {
        var errorCollection = (SqlErrorCollection)Activator.CreateInstance(
            typeof(SqlErrorCollection),
            nonPublic: true)!;
        var errorConstructor = typeof(SqlError)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .First();
        var errorArguments = errorConstructor.GetParameters()
            .Select(parameter => CreateArgument(parameter, number, errorCollection))
            .ToArray();
        var error = (SqlError)errorConstructor.Invoke(errorArguments);
        typeof(SqlErrorCollection)
            .GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(errorCollection, [error]);

        var factory = typeof(SqlException)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(method => method.Name == "CreateException")
            .Where(method => method.GetParameters().Length > 0
                && method.GetParameters()[0].ParameterType == typeof(SqlErrorCollection))
            .OrderBy(method => method.GetParameters().Length)
            .First();
        var factoryArguments = factory.GetParameters()
            .Select(parameter => CreateArgument(parameter, number, errorCollection))
            .ToArray();
        return (SqlException)factory.Invoke(null, factoryArguments)!;
    }

    private static object? CreateArgument(
        ParameterInfo parameter,
        int number,
        SqlErrorCollection errorCollection)
    {
        if (parameter.ParameterType == typeof(SqlErrorCollection))
        {
            return errorCollection;
        }

        if (parameter.ParameterType == typeof(int))
        {
            return parameter.Name?.Contains("number", StringComparison.OrdinalIgnoreCase) == true
                || parameter.Name?.Contains("info", StringComparison.OrdinalIgnoreCase) == true
                    ? number
                    : 0;
        }

        if (parameter.ParameterType == typeof(byte))
        {
            return (byte)0;
        }

        if (parameter.ParameterType == typeof(uint))
        {
            return 0U;
        }

        if (parameter.ParameterType == typeof(string))
        {
            return "simulated";
        }

        if (parameter.ParameterType == typeof(Guid))
        {
            return Guid.Empty;
        }

        if (parameter.ParameterType == typeof(bool))
        {
            return false;
        }

        return null;
    }
}

internal sealed class StubAccessTokenService : IAccessTokenService
{
    public (string Token, DateTimeOffset ExpiresAtUtc) Issue(string subject, string role, string displayName, string securityStamp) =>
        ($"token-{role}-{subject}", DateTimeOffset.UtcNow.AddMinutes(30));

    public Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken cancellationToken) =>
        Task.FromResult<ClaimsPrincipal?>(null);
}

internal sealed class CapturingEmailSender : IPasswordResetEmailSender
{
    public Uri? ResetLink { get; private set; }

    public Task SendAsync(string recipient, Uri resetLink, CancellationToken cancellationToken)
    {
        ResetLink = resetLink;
        return Task.CompletedTask;
    }
}
