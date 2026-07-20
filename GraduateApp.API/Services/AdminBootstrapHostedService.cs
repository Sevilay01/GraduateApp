using System.Security.Cryptography;
using System.Data;
using GraduateApp.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Services;

public sealed class AdminBootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AdminBootstrapHostedService> logger,
    TimeProvider timeProvider,
    IEmailNormalizer emailNormalizer) : IHostedService
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
        if (dbContext.Database.IsRelational()
            && !await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            logger.LogWarning("Bootstrap admin oluşturulamadı; veritabanına bağlanılamıyor.");
            return;
        }

        if (!emailNormalizer.TryNormalize(email, out var normalizedEmail))
        {
            throw new InvalidOperationException("Bootstrap admin e-posta adresi geçersiz.");
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var identity = await dbContext.LoginIdentities
            .Include(item => item.Admin)
            .Include(item => item.Student)
            .SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);
        if (identity is not null
            && identity.AccountType == LoginAccountType.Admin)
        {
            if (identity.AdminId is not null
                && identity.StudentTc is null
                && identity.Admin is not null
                && identity.Student is null
                && identity.NormalizedEmail == emailNormalizer.Normalize(identity.Admin.Email)
                && identity.Admin.NormalizedEmail == identity.NormalizedEmail)
            {
                return;
            }

            throw new InvalidOperationException(
                "Bootstrap admin hesabı oluşturulamadı: merkezi yönetici kimliği tutarsız.");
        }

        if (await dbContext.Admins.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Sistemde zaten bir yönetici hesabı var; yeni yönetici bootstrap üzerinden oluşturulamaz.");
        }

        if (identity is not null)
        {
            throw new InvalidOperationException(
                "Bootstrap admin hesabı oluşturulamadı: merkezi giriş kimliği başka bir hesapla çakışıyor.");
        }

        if (await dbContext.Students.AnyAsync(
                item => item.NormalizedEmail == normalizedEmail,
                cancellationToken)
            || await dbContext.Admins.AnyAsync(
                item => item.NormalizedEmail == normalizedEmail,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Bootstrap admin hesabı oluşturulamadı: hesap merkezi giriş kimliğiyle eşleşmiyor.");
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
        admin.LoginIdentity = new LoginIdentity
        {
            NormalizedEmail = normalizedEmail,
            AccountType = LoginAccountType.Admin,
            Admin = admin,
            CreatedAtUtc = now
        };
        dbContext.Admins.Add(admin);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException(
                "Bootstrap admin hesabı oluşturulamadı: merkezi giriş kimliği başka bir hesap tarafından kullanılıyor.");
        }

        logger.LogInformation("Bootstrap admin hesabı oluşturuldu; ilk girişte parola değişikliği zorunludur.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
