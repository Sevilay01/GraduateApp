using System.Data;
using System.Security.Cryptography;
using System.Text;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace GraduateApp.API.Services;

public interface IAdminAccountService
{
    Task<PagedResult<AdminAccountDto>> GetAsync(string? search, AdminAccountStatus? status, int page, int pageSize, int currentAdminId, CancellationToken cancellationToken);
    Task<AdminAccountDto?> GetByPublicIdAsync(Guid publicId, int currentAdminId, CancellationToken cancellationToken);
    Task<ServiceResult<AdminAccountDto>> InviteAsync(int actorAdminId, InviteAdminDto request, CancellationToken cancellationToken);
    Task<ServiceResult<AdminAccountDto>> ResendInvitationAsync(Guid publicId, int actorAdminId, AdminAccountConcurrencyDto request, CancellationToken cancellationToken);
    Task<ServiceResult<AdminAccountDto>> ActivateAsync(Guid publicId, int actorAdminId, AdminAccountConcurrencyDto request, CancellationToken cancellationToken);
    Task<ServiceResult<AdminAccountDto>> DeactivateAsync(Guid publicId, int actorAdminId, AdminAccountConcurrencyDto request, CancellationToken cancellationToken);
    Task<ServiceResult<AdminAccountDto>> UnlockAsync(Guid publicId, int actorAdminId, AdminAccountConcurrencyDto request, CancellationToken cancellationToken);
    Task<ServiceResult> AcceptInvitationAsync(AcceptAdminInvitationDto request, CancellationToken cancellationToken);
}

public sealed class AdminAccountService(
    GraduateAppDbContext dbContext,
    IPasswordHasher<Admin> passwordHasher,
    IEmailNormalizer emailNormalizer,
    IAdminInvitationEmailSender emailSender,
    IOptions<AdminInvitationOptions> invitationOptions,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<AdminAccountService> logger) : IAdminAccountService
{
    private const string ConcurrencyMessage = "Kayıt başka bir kullanıcı tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.";

    public async Task<PagedResult<AdminAccountDto>> GetAsync(
        string? search,
        AdminAccountStatus? status,
        int page,
        int pageSize,
        int currentAdminId,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var now = timeProvider.GetUtcNow();
        var query = dbContext.Admins.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(admin => admin.Email.Contains(term));
        }

        query = status switch
        {
            AdminAccountStatus.Active => query.Where(admin => admin.IsActive && !admin.IsInvitationPending && (admin.LockoutEndUtc == null || admin.LockoutEndUtc <= now)),
            AdminAccountStatus.Inactive => query.Where(admin => !admin.IsActive),
            AdminAccountStatus.InvitationPending => query.Where(admin => admin.IsInvitationPending),
            AdminAccountStatus.Locked => query.Where(admin => admin.LockoutEndUtc > now),
            _ => query
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(admin => admin.NormalizedEmail)
            .ThenBy(admin => admin.AdminId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<AdminAccountDto>(
            rows.Select(admin => Map(admin, currentAdminId, now)).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<AdminAccountDto?> GetByPublicIdAsync(
        Guid publicId,
        int currentAdminId,
        CancellationToken cancellationToken)
    {
        var admin = await dbContext.Admins.AsNoTracking().SingleOrDefaultAsync(
            item => item.PublicId == publicId,
            cancellationToken);
        return admin is null ? null : Map(admin, currentAdminId, timeProvider.GetUtcNow());
    }

    public async Task<ServiceResult<AdminAccountDto>> InviteAsync(
        int actorAdminId,
        InviteAdminDto request,
        CancellationToken cancellationToken)
    {
        if (!emailNormalizer.TryNormalize(request.Email, out var normalizedEmail))
        {
            return Failure("Geçerli bir e-posta adresi giriniz.", StatusCodes.Status400BadRequest);
        }

        await using var transaction = await BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        if (await dbContext.LoginIdentities.AnyAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken)
            || await dbContext.Students.AnyAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken)
            || await dbContext.Admins.AnyAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            return Failure("Bu e-posta adresiyle yeni bir yönetici daveti oluşturulamıyor.", StatusCodes.Status409Conflict);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var admin = new Admin
        {
            PublicId = Guid.NewGuid(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            IsActive = true,
            IsInvitationPending = true,
            MustChangePassword = false,
            SecurityStamp = NewSecurityStamp(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var unusablePassword = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        admin.PasswordHash = passwordHasher.HashPassword(admin, unusablePassword);
        admin.LoginIdentity = new LoginIdentity
        {
            NormalizedEmail = normalizedEmail,
            AccountType = LoginAccountType.Admin,
            Admin = admin,
            CreatedAtUtc = now
        };
        var (token, rawToken) = CreateInvitationToken(admin, now);
        admin.PasswordResetTokens.Add(token);
        dbContext.Admins.Add(admin);
        AddAudit(actorAdminId, "AdminInvited", admin.PublicId, "Yönetici daveti oluşturuldu.", now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException exception) when (DatabaseExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            return Failure("Bu e-posta adresiyle yeni bir yönetici daveti oluşturulamıyor.", StatusCodes.Status409Conflict);
        }

        await SendInvitationAsync(admin.Email, rawToken, cancellationToken);
        var result = await GetByPublicIdAsync(admin.PublicId, actorAdminId, cancellationToken);
        return ServiceResult<AdminAccountDto>.Success(result!, StatusCodes.Status201Created);
    }

    public async Task<ServiceResult<AdminAccountDto>> ResendInvitationAsync(
        Guid publicId,
        int actorAdminId,
        AdminAccountConcurrencyDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var admin = await dbContext.Admins.SingleOrDefaultAsync(item => item.PublicId == publicId, cancellationToken);
        if (admin is null)
        {
            return NotFound();
        }

        if (!admin.IsActive || !admin.IsInvitationPending)
        {
            return Failure("Yalnızca aktif ve bekleyen yönetici daveti yeniden gönderilebilir.", StatusCodes.Status409Conflict);
        }

        if (!TrySetOriginalRowVersion(admin, request.RowVersion))
        {
            return Failure("Yönetici eşzamanlılık bilgisi geçersizdir.", StatusCodes.Status400BadRequest);
        }

        var activeTokens = await dbContext.PasswordResetTokens
            .Where(item => item.AdminId == admin.AdminId
                && item.Purpose == PasswordResetTokenPurpose.AdminInvitation
                && !item.IsUsed)
            .ToListAsync(cancellationToken);
        foreach (var activeToken in activeTokens)
        {
            activeToken.IsUsed = true;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (token, rawToken) = CreateInvitationToken(admin, now);
        dbContext.PasswordResetTokens.Add(token);
        admin.UpdatedAtUtc = now;
        AddAudit(actorAdminId, "AdminInvitationResent", admin.PublicId, "Yönetici daveti yeniden oluşturuldu.", now);
        var saved = await SaveAndCommitAsync(transaction, cancellationToken);
        if (!saved.IsSuccess)
        {
            return Failure(saved.Error!, saved.StatusCode);
        }

        await SendInvitationAsync(admin.Email, rawToken, cancellationToken);
        return await CurrentResultAsync(admin.PublicId, actorAdminId, cancellationToken);
    }

    public Task<ServiceResult<AdminAccountDto>> ActivateAsync(
        Guid publicId,
        int actorAdminId,
        AdminAccountConcurrencyDto request,
        CancellationToken cancellationToken) =>
        SetActiveAsync(publicId, actorAdminId, true, request, cancellationToken);

    public Task<ServiceResult<AdminAccountDto>> DeactivateAsync(
        Guid publicId,
        int actorAdminId,
        AdminAccountConcurrencyDto request,
        CancellationToken cancellationToken) =>
        SetActiveAsync(publicId, actorAdminId, false, request, cancellationToken);

    public async Task<ServiceResult<AdminAccountDto>> UnlockAsync(
        Guid publicId,
        int actorAdminId,
        AdminAccountConcurrencyDto request,
        CancellationToken cancellationToken)
    {
        var admin = await dbContext.Admins.SingleOrDefaultAsync(item => item.PublicId == publicId, cancellationToken);
        if (admin is null)
        {
            return NotFound();
        }

        if (!TrySetOriginalRowVersion(admin, request.RowVersion))
        {
            return Failure("Yönetici eşzamanlılık bilgisi geçersizdir.", StatusCodes.Status400BadRequest);
        }

        if (admin.LockoutEndUtc is null && admin.AccessFailedCount == 0)
        {
            return Failure("Yönetici hesabı kilitli değil.", StatusCodes.Status409Conflict);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        admin.AccessFailedCount = 0;
        admin.LockoutEndUtc = null;
        admin.SecurityStamp = NewSecurityStamp();
        admin.UpdatedAtUtc = now;
        AddAudit(actorAdminId, "AdminUnlocked", admin.PublicId, "Yönetici hesabı kilidi açıldı.", now);
        var saved = await SaveAsync(cancellationToken);
        return saved.IsSuccess
            ? await CurrentResultAsync(admin.PublicId, actorAdminId, cancellationToken)
            : Failure(saved.Error!, saved.StatusCode);
    }

    public async Task<ServiceResult> AcceptInvitationAsync(
        AcceptAdminInvitationDto request,
        CancellationToken cancellationToken)
    {
        if (!PasswordPolicy.IsValid(request.NewPassword))
        {
            return ServiceResult.Failure(PasswordPolicy.ValidationMessage, StatusCodes.Status400BadRequest);
        }

        var tokenHash = HashToken(request.Token);
        var token = await dbContext.PasswordResetTokens
            .Include(item => item.Admin)
                .ThenInclude(admin => admin!.LoginIdentity)
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash
                && item.Purpose == PasswordResetTokenPurpose.AdminInvitation,
                cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (token?.Admin is null
            || token.IsUsed
            || token.ExpirationDate <= now
            || !token.Admin.IsActive
            || !token.Admin.IsInvitationPending)
        {
            return InvalidInvitation();
        }

        if (!IsIdentityConsistent(token.Admin))
        {
            AddAudit(null, "AdminIdentityIntegrityFailure", token.Admin.PublicId, "Yönetici merkezi kimlik doğrulaması başarısız oldu.", now);
            _ = await SaveAsync(cancellationToken);
            return InvalidInvitation();
        }

        var admin = token.Admin;
        admin.PasswordHash = passwordHasher.HashPassword(admin, request.NewPassword);
        admin.SecurityStamp = NewSecurityStamp();
        admin.IsInvitationPending = false;
        admin.AccessFailedCount = 0;
        admin.LockoutEndUtc = null;
        admin.MustChangePassword = false;
        admin.UpdatedAtUtc = now;
        var invitationTokens = await dbContext.PasswordResetTokens
            .Where(item => item.AdminId == admin.AdminId
                && item.Purpose == PasswordResetTokenPurpose.AdminInvitation
                && !item.IsUsed)
            .ToListAsync(cancellationToken);
        foreach (var invitationToken in invitationTokens)
        {
            invitationToken.IsUsed = true;
        }

        AddAudit(null, "AdminInvitationAccepted", admin.PublicId, "Yönetici daveti kabul edildi.", now);
        var saved = await SaveAsync(cancellationToken);
        return saved.IsSuccess
            ? ServiceResult.Success()
            : ServiceResult.Failure(saved.Error!, saved.StatusCode);
    }

    private async Task<ServiceResult<AdminAccountDto>> SetActiveAsync(
        Guid publicId,
        int actorAdminId,
        bool isActive,
        AdminAccountConcurrencyDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
        List<Admin>? lockedActiveAdmins = null;
        if (!isActive && dbContext.Database.IsSqlServer())
        {
            lockedActiveAdmins = await dbContext.Admins
                .FromSqlRaw("SELECT * FROM [dbo].[Admins] WITH (UPDLOCK, HOLDLOCK) WHERE [IsActive] = 1 AND [IsInvitationPending] = 0")
                .ToListAsync(cancellationToken);
        }

        var admin = lockedActiveAdmins?.SingleOrDefault(item => item.PublicId == publicId)
            ?? await dbContext.Admins.SingleOrDefaultAsync(item => item.PublicId == publicId, cancellationToken);
        if (admin is null)
        {
            return NotFound();
        }

        if (admin.IsActive == isActive)
        {
            return Failure(isActive ? "Yönetici zaten aktif." : "Yönetici zaten pasif.", StatusCodes.Status409Conflict);
        }

        if (!TrySetOriginalRowVersion(admin, request.RowVersion))
        {
            return Failure("Yönetici eşzamanlılık bilgisi geçersizdir.", StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (!isActive && admin.AdminId == actorAdminId)
        {
            AddAudit(actorAdminId, "SelfDeactivationRejected", admin.PublicId, "Kendi hesabını pasifleştirme isteği reddedildi.", now);
            await SaveAndCommitAsync(transaction, cancellationToken);
            return Failure("Kendi yönetici hesabınızı pasifleştiremezsiniz.", StatusCodes.Status409Conflict);
        }

        if (!isActive && !admin.IsInvitationPending)
        {
            var activeCompletedCount = lockedActiveAdmins?.Count
                ?? await dbContext.Admins.CountAsync(item => item.IsActive && !item.IsInvitationPending, cancellationToken);
            if (activeCompletedCount <= 1)
            {
                AddAudit(actorAdminId, "LastActiveAdminProtectionTriggered", admin.PublicId, "Son aktif yönetici koruması işlemi engelledi.", now);
                AddAudit(actorAdminId, "AdminActivationRejected", admin.PublicId, "Yönetici pasifleştirme isteği güvenlik kuralıyla reddedildi.", now);
                await SaveAndCommitAsync(transaction, cancellationToken);
                return Failure("Sistemde en az bir aktif ve daveti tamamlanmış yönetici kalmalıdır.", StatusCodes.Status409Conflict);
            }
        }

        admin.IsActive = isActive;
        admin.SecurityStamp = NewSecurityStamp();
        admin.AccessFailedCount = 0;
        admin.LockoutEndUtc = null;
        admin.UpdatedAtUtc = now;
        if (!isActive)
        {
            var activeTokens = await dbContext.PasswordResetTokens
                .Where(item => item.AdminId == admin.AdminId && !item.IsUsed)
                .ToListAsync(cancellationToken);
            foreach (var activeToken in activeTokens)
            {
                activeToken.IsUsed = true;
            }
        }

        AddAudit(
            actorAdminId,
            isActive ? "AdminActivated" : "AdminDeactivated",
            admin.PublicId,
            isActive ? "Yönetici hesabı aktifleştirildi." : "Yönetici hesabı pasifleştirildi ve oturumları iptal edildi.",
            now);
        var saved = await SaveAndCommitAsync(transaction, cancellationToken);
        return saved.IsSuccess
            ? await CurrentResultAsync(admin.PublicId, actorAdminId, cancellationToken)
            : Failure(saved.Error!, saved.StatusCode);
    }

    private (PasswordResetToken Token, string RawToken) CreateInvitationToken(Admin admin, DateTime now)
    {
        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (new PasswordResetToken
        {
            Admin = admin,
            AdminId = admin.AdminId == 0 ? null : admin.AdminId,
            Purpose = PasswordResetTokenPurpose.AdminInvitation,
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = now,
            ExpirationDate = now.AddHours(invitationOptions.Value.LifetimeHours),
            IsUsed = false
        }, rawToken);
    }

    private async Task SendInvitationAsync(string email, string rawToken, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(email, CreateInvitationLink(rawToken), cancellationToken);
        }
        catch (Exception)
        {
            logger.LogError(new EventId(1101, "AdminInvitationNotificationFailure"), "Admin invitation notification could not be sent.");
        }
    }

    private Uri CreateInvitationLink(string rawToken)
    {
        var baseUrl = configuration["Web:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var webBaseUri)
            || (webBaseUri.Scheme != Uri.UriSchemeHttps && webBaseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("Web:BaseUrl yapılandırması geçersiz.");
        }

        var url = new Uri(webBaseUri, "/Account/AcceptAdminInvitation").ToString();
        return new Uri(QueryHelpers.AddQueryString(url, "token", rawToken));
    }

    private bool TrySetOriginalRowVersion(Admin admin, string encoded)
    {
        try
        {
            var rowVersion = Convert.FromBase64String(encoded);
            if (rowVersion.Length != 8)
            {
                return false;
            }

            dbContext.Entry(admin).Property(item => item.RowVersion).OriginalValue = rowVersion;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private bool IsIdentityConsistent(Admin admin) => admin.LoginIdentity is
    {
        AccountType: LoginAccountType.Admin,
        StudentTc: null,
        AdminId: not null
    } identity
        && identity.AdminId == admin.AdminId
        && identity.NormalizedEmail == admin.NormalizedEmail
        && identity.NormalizedEmail == emailNormalizer.Normalize(admin.Email);

    private void AddAudit(int? actorAdminId, string eventType, Guid targetId, string details, DateTime now) =>
        dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorAdminId = actorAdminId,
            EventType = eventType,
            TargetType = "Admin",
            TargetId = targetId.ToString("D"),
            Details = details,
            CreatedAtUtc = now
        });

    private async Task<ServiceResult<AdminAccountDto>> CurrentResultAsync(Guid publicId, int actorAdminId, CancellationToken cancellationToken)
    {
        var value = await GetByPublicIdAsync(publicId, actorAdminId, cancellationToken);
        return value is null ? NotFound() : ServiceResult<AdminAccountDto>.Success(value);
    }

    private async Task<ServiceResult> SaveAndCommitAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        var result = await SaveAsync(cancellationToken);
        if (result.IsSuccess)
        {
            await CommitAsync(transaction, cancellationToken);
        }

        return result;
    }

    private async Task<ServiceResult> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Failure(ConcurrencyMessage, StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException exception) when (DatabaseExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            return ServiceResult.Failure("Bu bilgilerle yönetici hesabı güncellenemiyor.", StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException)
        {
            return ServiceResult.Failure("Yönetici hesabı veri bütünlüğü nedeniyle güncellenemedi.", StatusCodes.Status409Conflict);
        }
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(CancellationToken cancellationToken) =>
        await BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken)
            : null;

    private static Task CommitAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);

    private static AdminAccountDto Map(Admin admin, int currentAdminId, DateTimeOffset now) => new(
        admin.PublicId,
        admin.Email,
        admin.IsActive,
        admin.IsInvitationPending,
        admin.LockoutEndUtc > now,
        admin.AccessFailedCount,
        admin.AdminId == currentAdminId,
        admin.CreatedAtUtc,
        admin.UpdatedAtUtc,
        Convert.ToBase64String(admin.RowVersion));

    private static ServiceResult<AdminAccountDto> Failure(string error, int statusCode) =>
        ServiceResult<AdminAccountDto>.Failure(error, statusCode);

    private static ServiceResult<AdminAccountDto> NotFound() =>
        Failure("Yönetici hesabı bulunamadı.", StatusCodes.Status404NotFound);

    private static ServiceResult InvalidInvitation() =>
        ServiceResult.Failure("Yönetici daveti geçersiz, kullanılmış veya süresi dolmuş.", StatusCodes.Status400BadRequest);

    private static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
