using System.Text;

namespace GraduateApp.API.Services;

public interface IAdminInvitationEmailSender
{
    Task SendAsync(string recipient, Uri invitationLink, CancellationToken cancellationToken);
}

public sealed class DevelopmentFileAdminInvitationEmailSender(IHostEnvironment environment)
    : IAdminInvitationEmailSender
{
    public async Task SendAsync(string recipient, Uri invitationLink, CancellationToken cancellationToken)
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
        var fileName = $"admin-invitation-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.txt";
        var body = $"To: {recipient}{Environment.NewLine}Subject: GraduateApp yönetici daveti{Environment.NewLine}{Environment.NewLine}Yönetici hesabınızı etkinleştirmek ve parolanızı belirlemek için aşağıdaki tek kullanımlık bağlantıyı kullanın:{Environment.NewLine}{invitationLink}";
        await File.WriteAllTextAsync(Path.Combine(outbox, fileName), body, Encoding.UTF8, cancellationToken);
    }
}

public sealed class UnavailableAdminInvitationEmailSender(ILogger<UnavailableAdminInvitationEmailSender> logger)
    : IAdminInvitationEmailSender
{
    public Task SendAsync(string recipient, Uri invitationLink, CancellationToken cancellationToken)
    {
        logger.LogError("Admin invitation e-posta sağlayıcısı yapılandırılmamış.");
        throw new InvalidOperationException("Admin invitation e-posta sağlayıcısı yapılandırılmamış.");
    }
}
