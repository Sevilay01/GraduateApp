using System.Security.Claims;
using GraduateApp.API.Models;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

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
