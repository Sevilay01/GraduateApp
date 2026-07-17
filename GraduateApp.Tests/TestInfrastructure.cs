using System.Security.Claims;
using GraduateApp.API.Models;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.Tests;

internal static class TestDb
{
    public static GraduateAppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new GraduateAppDbContext(options);
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
