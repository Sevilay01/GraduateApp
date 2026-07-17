using System.Text;

namespace GraduateApp.API.Services;

public interface IPasswordResetEmailSender
{
    Task SendAsync(string recipient, Uri resetLink, CancellationToken cancellationToken);
}

public sealed class DevelopmentFilePasswordResetEmailSender(IHostEnvironment environment)
    : IPasswordResetEmailSender
{
    public async Task SendAsync(string recipient, Uri resetLink, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Development e-posta göndericisi production ortamında kullanılamaz.");
        }

        var outbox = Path.GetFullPath(Path.Combine(environment.ContentRootPath, ".dev-emails"));
        var contentRoot = Path.GetFullPath(environment.ContentRootPath) + Path.DirectorySeparatorChar;
        if (!outbox.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Development e-posta dizini geçersiz.");
        }

        Directory.CreateDirectory(outbox);
        var fileName = $"password-reset-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.txt";
        var body = $"To: {recipient}{Environment.NewLine}Subject: GraduateApp parola sıfırlama{Environment.NewLine}{Environment.NewLine}Parolanızı sıfırlamak için aşağıdaki tek kullanımlık bağlantıyı kullanın:{Environment.NewLine}{resetLink}";
        await File.WriteAllTextAsync(Path.Combine(outbox, fileName), body, Encoding.UTF8, cancellationToken);
    }
}

public sealed class UnavailablePasswordResetEmailSender(ILogger<UnavailablePasswordResetEmailSender> logger)
    : IPasswordResetEmailSender
{
    public Task SendAsync(string recipient, Uri resetLink, CancellationToken cancellationToken)
    {
        logger.LogError("Password reset e-posta sağlayıcısı yapılandırılmamış.");
        throw new InvalidOperationException("Password reset e-posta sağlayıcısı yapılandırılmamış.");
    }
}
