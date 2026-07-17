using System.Security.Cryptography;
using GraduateApp.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Services;

public sealed class AdminBootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AdminBootstrapHostedService> logger,
    TimeProvider timeProvider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var email = configuration["BootstrapAdmin:Email"];
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Bootstrap admin e-posta ve parola değerleri birlikte sağlanmalıdır.");
        }

        if (!PasswordPolicy.IsValid(password))
        {
            throw new InvalidOperationException($"Bootstrap admin parolası geçersiz. {PasswordPolicy.ValidationMessage}");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GraduateAppDbContext>();
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            logger.LogWarning("Bootstrap admin oluşturulamadı; veritabanına bağlanılamıyor.");
            return;
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();
        if (await dbContext.Admins.AnyAsync(
            admin => admin.NormalizedEmail == normalizedEmail || admin.Email.ToUpper() == normalizedEmail,
            cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var admin = new Admin
        {
            Email = email.Trim(),
            NormalizedEmail = normalizedEmail,
            SecurityStamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            MustChangePassword = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Admin>>();
        admin.PasswordHash = hasher.HashPassword(admin, password);
        dbContext.Admins.Add(admin);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Bootstrap admin hesabı oluşturuldu; ilk girişte parola değişikliği zorunludur.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
