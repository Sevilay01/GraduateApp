using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using GraduateApp.API.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Security;

public interface IAccessTokenService
{
    (string Token, DateTimeOffset ExpiresAtUtc) Issue(string subject, string role, string displayName, string securityStamp);
    Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken cancellationToken);
}

public sealed class AccessTokenService : IAccessTokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    private readonly ITimeLimitedDataProtector _protector;
    private readonly GraduateAppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public AccessTokenService(
        IDataProtectionProvider dataProtectionProvider,
        GraduateAppDbContext dbContext,
        TimeProvider timeProvider)
    {
        _protector = dataProtectionProvider
            .CreateProtector("GraduateApp.Api.AccessToken.v1")
            .ToTimeLimitedDataProtector();
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public (string Token, DateTimeOffset ExpiresAtUtc) Issue(
        string subject,
        string role,
        string displayName,
        string securityStamp)
    {
        var expiresAt = _timeProvider.GetUtcNow().Add(TokenLifetime);
        var payload = new AccessTokenPayload(subject, role, displayName, securityStamp);
        var token = _protector.Protect(JsonSerializer.Serialize(payload), TokenLifetime);
        return (token, expiresAt);
    }

    public async Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        AccessTokenPayload? payload;
        DateTimeOffset expiresAt;

        try
        {
            var json = _protector.Unprotect(token, out expiresAt);
            payload = JsonSerializer.Deserialize<AccessTokenPayload>(json);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }

        if (payload is null || expiresAt <= _timeProvider.GetUtcNow())
        {
            return null;
        }

        var isCurrent = payload.Role switch
        {
            ApiAuthenticationDefaults.StudentRole => await _dbContext.Students.AsNoTracking().AnyAsync(
                student => student.Tc == payload.Subject
                    && student.IsActive
                    && student.SecurityStamp == payload.SecurityStamp
                    && (student.LockoutEndUtc == null || student.LockoutEndUtc <= _timeProvider.GetUtcNow()),
                cancellationToken),
            ApiAuthenticationDefaults.AdminRole when int.TryParse(payload.Subject, out var adminId) =>
                await _dbContext.Admins.AsNoTracking().AnyAsync(
                    admin => admin.AdminId == adminId
                        && admin.SecurityStamp == payload.SecurityStamp
                        && (admin.LockoutEndUtc == null || admin.LockoutEndUtc <= _timeProvider.GetUtcNow()),
                    cancellationToken),
            _ => false
        };

        if (!isCurrent)
        {
            return null;
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, payload.Subject),
            new Claim(ClaimTypes.Name, payload.DisplayName),
            new Claim(ClaimTypes.Role, payload.Role),
            new Claim(ApiAuthenticationDefaults.SecurityStampClaim, payload.SecurityStamp)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, ApiAuthenticationDefaults.Scheme));
    }

    private sealed record AccessTokenPayload(
        string Subject,
        string Role,
        string DisplayName,
        string SecurityStamp);
}
