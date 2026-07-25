using System.Security.Cryptography;
using System.Data;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Services;

public interface IAuthService
{
    Task<ServiceResult> RegisterStudentAsync(RegisterStudentDto request, CancellationToken cancellationToken);
    Task<ServiceResult<LoginResponse>> LoginAsync(LoginDto request, CancellationToken cancellationToken);
    Task RequestPasswordResetAsync(ForgotPasswordDto request, CancellationToken cancellationToken);
    Task<ServiceResult<PasswordResetResponse>> ResetPasswordAsync(ResetPasswordDto request, CancellationToken cancellationToken);
    Task<ServiceResult> ChangePasswordAsync(string subject, string role, ChangePasswordDto request, CancellationToken cancellationToken);
    Task RevokeSessionsAsync(string subject, string role, CancellationToken cancellationToken);
}

public sealed class AuthService(
    GraduateAppDbContext dbContext,
    IPasswordHasher<Student> studentPasswordHasher,
    IPasswordHasher<Admin> adminPasswordHasher,
    IAccessTokenService accessTokenService,
    IPasswordResetEmailSender emailSender,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<AuthService> logger,
    StudentRegistrationValidator registrationValidator,
    IEmailNormalizer emailNormalizer) : IAuthService
{
    private const int MaximumFailedAttempts = 5;
    private static readonly EventId StudentRegistrationDatabaseFailure = new(1001, nameof(StudentRegistrationDatabaseFailure));
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromMinutes(30);

    public async Task<ServiceResult> RegisterStudentAsync(
        RegisterStudentDto request,
        CancellationToken cancellationToken)
    {
        if (!TurkishIdentityNumberValidator.IsValid(request.Tc))
        {
            return ServiceResult.Failure("TC kimlik numarası doğrulanamadı.", StatusCodes.Status400BadRequest);
        }

        if (!PasswordPolicy.IsValid(request.Password))
        {
            return ServiceResult.Failure(PasswordPolicy.ValidationMessage, StatusCodes.Status400BadRequest);
        }

        var registration = registrationValidator.Validate(request);
        if (!registration.IsValid || registration.Value is null)
        {
            return ServiceResult.Failure(
                registration.Error ?? "Kayıt bilgileri doğrulanamadı.",
                StatusCodes.Status400BadRequest);
        }

        var normalized = registration.Value;
        if (!emailNormalizer.TryNormalize(normalized.Email, out var normalizedEmail))
        {
            return ServiceResult.Failure(
                "Geçerli bir e-posta adresi giriniz.",
                StatusCodes.Status400BadRequest);
        }
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        var identityExists = await dbContext.LoginIdentities.AnyAsync(
            identity => identity.NormalizedEmail == normalizedEmail,
            cancellationToken);
        var unlinkedAccountExists = await dbContext.Students.AnyAsync(
            student => student.NormalizedEmail == normalizedEmail,
            cancellationToken)
            || await dbContext.Admins.AnyAsync(
                admin => admin.NormalizedEmail == normalizedEmail,
                cancellationToken);
        var studentExists = await dbContext.Students.AnyAsync(
            student => student.Tc == request.Tc || student.Telephone == normalized.Telephone,
            cancellationToken);

        if (identityExists || unlinkedAccountExists || studentExists)
        {
            return ServiceResult.Failure("Bu bilgilerle kayıt oluşturulamıyor.", StatusCodes.Status409Conflict);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var student = new Student
        {
            Tc = request.Tc,
            PublicId = Guid.NewGuid(),
            StudentName = normalized.FirstName,
            StudentSurname = normalized.LastName,
            FatherName = normalized.FatherName,
            BirthDate = normalized.BirthDate,
            Email = normalized.Email,
            NormalizedEmail = normalizedEmail,
            Telephone = normalized.Telephone,
            SecurityStamp = NewSecurityStamp(),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        student.PasswordHash = studentPasswordHasher.HashPassword(student, request.Password);
        student.LoginIdentity = new LoginIdentity
        {
            NormalizedEmail = normalizedEmail,
            AccountType = LoginAccountType.Student,
            Student = student,
            CreatedAtUtc = now
        };

        dbContext.Students.Add(student);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ServiceResult.Success(StatusCodes.Status201Created);
        }
        catch (DbUpdateException exception) when (DatabaseExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            return ServiceResult.Failure("Bu bilgilerle kayıt oluşturulamıyor.", StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            logger.LogError(
                StudentRegistrationDatabaseFailure,
                "Student registration failed because the database operation could not be completed.");
            return ServiceResult.Failure(
                "Kayıt şu anda oluşturulamıyor. Lütfen daha sonra tekrar deneyin.",
                StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<ServiceResult<LoginResponse>> LoginAsync(LoginDto request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var normalizedEmail = emailNormalizer.Normalize(username);
        var now = timeProvider.GetUtcNow();

        if (request.AccountType is not (LoginAccountType.Student or LoginAccountType.Admin))
        {
            PerformDummyPasswordVerification(request.Password, LoginAccountType.Student);
            return InvalidLogin<LoginResponse>();
        }

        IQueryable<LoginIdentity> identitiesQuery = dbContext.LoginIdentities
            .Include(identity => identity.Student)
            .Include(identity => identity.Admin);
        identitiesQuery = request.AccountType == LoginAccountType.Admin
            ? identitiesQuery.Where(identity => identity.AccountType == LoginAccountType.Admin
                && identity.NormalizedEmail == normalizedEmail)
            : identitiesQuery.Where(identity => identity.AccountType == LoginAccountType.Student
                && (identity.StudentTc == username || identity.NormalizedEmail == normalizedEmail));

        var identities = await identitiesQuery
            .OrderBy(identity => identity.LoginIdentityId)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (identities.Count != 1)
        {
            if (identities.Count > 1)
            {
                await RecordIdentityIntegrityFailureAsync(identities[0], cancellationToken);
            }

            PerformDummyPasswordVerification(request.Password, request.AccountType.Value);
            return InvalidLogin<LoginResponse>();
        }

        var identity = identities[0];
        if (identity.AccountType != request.AccountType)
        {
            PerformDummyPasswordVerification(request.Password, request.AccountType.Value);
            return InvalidLogin<LoginResponse>();
        }

        if (!IsIdentityConsistent(identity))
        {
            await RecordIdentityIntegrityFailureAsync(identity, cancellationToken);
            PerformDummyPasswordVerification(request.Password, request.AccountType.Value);
            return InvalidLogin<LoginResponse>();
        }

        return identity.AccountType == LoginAccountType.Student
            ? await LoginStudentAsync(identity.Student!, request.Password, now, cancellationToken)
            : await LoginAdminAsync(identity.Admin!, request.Password, now, cancellationToken);
    }

    public async Task RequestPasswordResetAsync(ForgotPasswordDto request, CancellationToken cancellationToken)
    {
        if (!emailNormalizer.TryNormalize(request.Email, out var normalizedEmail))
        {
            PerformDummyPasswordVerification(Guid.NewGuid().ToString("N"), LoginAccountType.Student);
            return;
        }
        var identities = await dbContext.LoginIdentities
            .Include(identity => identity.Student)
            .Include(identity => identity.Admin)
            .Where(identity => identity.NormalizedEmail == normalizedEmail)
            .OrderBy(identity => identity.LoginIdentityId)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (identities.Count != 1)
        {
            if (identities.Count > 1)
            {
                await RecordIdentityIntegrityFailureAsync(identities[0], cancellationToken);
            }

            PerformDummyPasswordVerification(Guid.NewGuid().ToString("N"), LoginAccountType.Student);
            return;
        }

        var identity = identities[0];
        if (!IsIdentityConsistent(identity))
        {
            await RecordIdentityIntegrityFailureAsync(identity, cancellationToken);
            PerformDummyPasswordVerification(Guid.NewGuid().ToString("N"), identity.AccountType);
            return;
        }

        var student = identity.Student;
        var admin = identity.Admin;
        if (student is { IsActive: false })
        {
            PerformDummyPasswordVerification(Guid.NewGuid().ToString("N"), LoginAccountType.Student);
            return;
        }

        if (admin is { IsActive: false } or { IsInvitationPending: true })
        {
            PerformDummyPasswordVerification(Guid.NewGuid().ToString("N"), LoginAccountType.Admin);
            return;
        }

        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var token = new PasswordResetToken
        {
            Tc = student?.Tc,
            AdminId = admin?.AdminId,
            Purpose = PasswordResetTokenPurpose.PasswordReset,
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = now,
            ExpirationDate = now.Add(ResetTokenLifetime),
            IsUsed = false
        };

        var activeTokens = await dbContext.PasswordResetTokens
            .Where(item => item.Purpose == PasswordResetTokenPurpose.PasswordReset
                && !item.IsUsed
                && ((student != null && item.Tc == student.Tc)
                    || (admin != null && item.AdminId == admin.AdminId)))
            .ToListAsync(cancellationToken);
        foreach (var activeToken in activeTokens)
        {
            activeToken.IsUsed = true;
        }

        dbContext.PasswordResetTokens.Add(token);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var resetLink = CreateResetLink(rawToken);
            await emailSender.SendAsync(student?.Email ?? admin!.Email, resetLink, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                new EventId(1004, "PasswordResetNotificationFailure"),
                "Password reset notification could not be sent.");
        }
    }

    public async Task<ServiceResult<PasswordResetResponse>> ResetPasswordAsync(
        ResetPasswordDto request,
        CancellationToken cancellationToken)
    {
        if (!PasswordPolicy.IsValid(request.NewPassword))
        {
            return ServiceResult<PasswordResetResponse>.Failure(
                PasswordPolicy.ValidationMessage,
                StatusCodes.Status400BadRequest);
        }

        var tokenHash = HashToken(request.Token);
        var token = await dbContext.PasswordResetTokens
            .Include(item => item.TcNavigation)
                .ThenInclude(student => student!.LoginIdentity)
            .Include(item => item.Admin)
                .ThenInclude(admin => admin!.LoginIdentity)
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash
                && item.Purpose == PasswordResetTokenPurpose.PasswordReset,
                cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (token is null
            || token.IsUsed
            || token.ExpirationDate <= now
            || token.TcNavigation is { IsActive: false }
            || token.Admin is { IsActive: false }
            || token.Admin is { IsInvitationPending: true })
        {
            return InvalidPasswordReset();
        }

        var loginIdentity = token.TcNavigation?.LoginIdentity ?? token.Admin?.LoginIdentity;
        if (loginIdentity is null || !IsIdentityConsistent(loginIdentity))
        {
            if (loginIdentity is not null)
            {
                await RecordIdentityIntegrityFailureAsync(loginIdentity, cancellationToken);
            }

            return InvalidPasswordReset();
        }

        if (token.TcNavigation is not null)
        {
            token.TcNavigation.PasswordHash = studentPasswordHasher.HashPassword(token.TcNavigation, request.NewPassword);
            token.TcNavigation.SecurityStamp = NewSecurityStamp();
            token.TcNavigation.AccessFailedCount = 0;
            token.TcNavigation.LockoutEndUtc = null;
            token.TcNavigation.UpdatedAtUtc = now;
        }
        else if (token.Admin is not null)
        {
            token.Admin.PasswordHash = adminPasswordHasher.HashPassword(token.Admin, request.NewPassword);
            token.Admin.SecurityStamp = NewSecurityStamp();
            token.Admin.AccessFailedCount = 0;
            token.Admin.LockoutEndUtc = null;
            token.Admin.MustChangePassword = false;
            token.Admin.UpdatedAtUtc = now;
        }
        else
        {
            return InvalidPasswordReset();
        }

        var relatedTokens = await dbContext.PasswordResetTokens
            .Where(item => item.Purpose == PasswordResetTokenPurpose.PasswordReset
                && !item.IsUsed
                && ((token.Tc != null && item.Tc == token.Tc)
                    || (token.AdminId != null && item.AdminId == token.AdminId)))
            .ToListAsync(cancellationToken);
        foreach (var relatedToken in relatedTokens)
        {
            relatedToken.IsUsed = true;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult<PasswordResetResponse>.Success(
                new PasswordResetResponse(loginIdentity.AccountType));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<PasswordResetResponse>.Failure(
                "Parola sıfırlama bağlantısı daha önce kullanılmış.",
                StatusCodes.Status409Conflict);
        }
    }

    public async Task<ServiceResult> ChangePasswordAsync(
        string subject,
        string role,
        ChangePasswordDto request,
        CancellationToken cancellationToken)
    {
        if (!PasswordPolicy.IsValid(request.NewPassword))
        {
            return ServiceResult.Failure(PasswordPolicy.ValidationMessage, StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (role == ApiAuthenticationDefaults.StudentRole)
        {
            var student = await dbContext.Students.SingleOrDefaultAsync(item => item.Tc == subject, cancellationToken);
            if (student is null || studentPasswordHasher.VerifyHashedPassword(student, student.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            {
                return ServiceResult.Failure("Mevcut parola doğrulanamadı.", StatusCodes.Status400BadRequest);
            }

            student.PasswordHash = studentPasswordHasher.HashPassword(student, request.NewPassword);
            student.SecurityStamp = NewSecurityStamp();
            student.UpdatedAtUtc = now;
        }
        else if (role == ApiAuthenticationDefaults.AdminRole && int.TryParse(subject, out var adminId))
        {
            var admin = await dbContext.Admins.SingleOrDefaultAsync(item => item.AdminId == adminId, cancellationToken);
            if (admin is null
                || !admin.IsActive
                || admin.IsInvitationPending
                || adminPasswordHasher.VerifyHashedPassword(admin, admin.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            {
                return ServiceResult.Failure("Mevcut parola doğrulanamadı.", StatusCodes.Status400BadRequest);
            }

            admin.PasswordHash = adminPasswordHasher.HashPassword(admin, request.NewPassword);
            admin.SecurityStamp = NewSecurityStamp();
            admin.MustChangePassword = false;
            admin.UpdatedAtUtc = now;
        }
        else
        {
            return ServiceResult.Failure("Kullanıcı doğrulanamadı.", StatusCodes.Status401Unauthorized);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult.Success();
    }

    public async Task RevokeSessionsAsync(string subject, string role, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (role == ApiAuthenticationDefaults.StudentRole)
        {
            var student = await dbContext.Students.SingleOrDefaultAsync(item => item.Tc == subject, cancellationToken);
            if (student is null)
            {
                return;
            }

            student.SecurityStamp = NewSecurityStamp();
            student.UpdatedAtUtc = now;
        }
        else if (role == ApiAuthenticationDefaults.AdminRole && int.TryParse(subject, out var adminId))
        {
            var admin = await dbContext.Admins.SingleOrDefaultAsync(item => item.AdminId == adminId, cancellationToken);
            if (admin is null)
            {
                return;
            }

            admin.SecurityStamp = NewSecurityStamp();
            admin.UpdatedAtUtc = now;
        }
        else
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ServiceResult<LoginResponse>> LoginStudentAsync(
        Student student,
        string password,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!student.IsActive)
        {
            PerformDummyPasswordVerification(password, LoginAccountType.Student);
            return InvalidLogin<LoginResponse>();
        }

        if (student.LockoutEndUtc > now)
        {
            PerformDummyPasswordVerification(password, LoginAccountType.Student);
            return InvalidLogin<LoginResponse>();
        }

        var verification = studentPasswordHasher.VerifyHashedPassword(student, student.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            await RecordStudentFailureAsync(student, now, cancellationToken);
            return InvalidLogin<LoginResponse>();
        }

        student.AccessFailedCount = 0;
        student.LockoutEndUtc = null;
        student.UpdatedAtUtc = now.UtcDateTime;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            student.PasswordHash = studentPasswordHasher.HashPassword(student, password);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var displayName = $"{student.StudentName} {student.StudentSurname}".Trim();
        var issued = accessTokenService.Issue(
            student.Tc,
            ApiAuthenticationDefaults.StudentRole,
            displayName,
            student.SecurityStamp);
        return ServiceResult<LoginResponse>.Success(new LoginResponse(
            issued.Token,
            issued.ExpiresAtUtc,
            ApiAuthenticationDefaults.StudentRole,
            displayName,
            false));
    }

    private async Task<ServiceResult<LoginResponse>> LoginAdminAsync(
        Admin admin,
        string password,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (admin.LockoutEndUtc > now)
        {
            PerformDummyPasswordVerification(password, LoginAccountType.Admin);
            return InvalidLogin<LoginResponse>();
        }

        if (!admin.IsActive || admin.IsInvitationPending)
        {
            PerformDummyPasswordVerification(password, LoginAccountType.Admin);
            return InvalidLogin<LoginResponse>();
        }

        var verification = adminPasswordHasher.VerifyHashedPassword(admin, admin.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            admin.AccessFailedCount++;
            if (admin.AccessFailedCount >= MaximumFailedAttempts)
            {
                admin.LockoutEndUtc = now.Add(LockoutDuration);
                admin.AccessFailedCount = 0;
            }

            admin.UpdatedAtUtc = now.UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);
            return InvalidLogin<LoginResponse>();
        }

        admin.AccessFailedCount = 0;
        admin.LockoutEndUtc = null;
        admin.UpdatedAtUtc = now.UtcDateTime;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            admin.PasswordHash = adminPasswordHasher.HashPassword(admin, password);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var issued = accessTokenService.Issue(
            admin.AdminId.ToString(),
            ApiAuthenticationDefaults.AdminRole,
            "Yönetici",
            admin.SecurityStamp);
        return ServiceResult<LoginResponse>.Success(new LoginResponse(
            issued.Token,
            issued.ExpiresAtUtc,
            ApiAuthenticationDefaults.AdminRole,
            "Yönetici",
            admin.MustChangePassword));
    }

    private bool IsIdentityConsistent(LoginIdentity identity) => identity.AccountType switch
    {
        LoginAccountType.Student => identity.StudentTc is not null
            && identity.AdminId is null
            && identity.Student is not null
            && identity.Admin is null
            && identity.NormalizedEmail == emailNormalizer.Normalize(identity.Student.Email)
            && identity.Student.NormalizedEmail == identity.NormalizedEmail,
        LoginAccountType.Admin => identity.StudentTc is null
            && identity.AdminId is not null
            && identity.Student is null
            && identity.Admin is not null
            && identity.NormalizedEmail == emailNormalizer.Normalize(identity.Admin.Email)
            && identity.Admin.NormalizedEmail == identity.NormalizedEmail,
        _ => false
    };

    private async Task RecordIdentityIntegrityFailureAsync(LoginIdentity identity, CancellationToken cancellationToken)
    {
        var isAdminIdentity = identity.AccountType == LoginAccountType.Admin;
        var eventType = isAdminIdentity ? "AdminIdentityIntegrityFailure" : "LoginIdentityIntegrityFailure";
        logger.LogWarning(
            new EventId(1002, eventType),
            "Login identity integrity validation failed for identity id {IdentityId}.",
            identity.LoginIdentityId);
        dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            EventType = eventType,
            TargetType = isAdminIdentity ? "Admin" : "LoginIdentity",
            TargetId = isAdminIdentity && identity.Admin is not null
                ? identity.Admin.PublicId.ToString("D")
                : identity.LoginIdentityId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Details = "Merkezi giriş kimliği ilişki doğrulamasını geçemedi.",
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            logger.LogError(
                new EventId(1003, "LoginIdentityAuditFailure"),
                "Login identity integrity audit record could not be persisted.");
        }
    }

    private async Task RecordStudentFailureAsync(Student student, DateTimeOffset now, CancellationToken cancellationToken)
    {
        student.AccessFailedCount++;
        if (student.AccessFailedCount >= MaximumFailedAttempts)
        {
            student.LockoutEndUtc = now.Add(LockoutDuration);
            student.AccessFailedCount = 0;
        }

        student.UpdatedAtUtc = now.UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void PerformDummyPasswordVerification(string password, LoginAccountType accountType)
    {
        if (accountType == LoginAccountType.Admin)
        {
            var dummyAdmin = new Admin();
            var dummyHash = adminPasswordHasher.HashPassword(dummyAdmin, "Dummy-Password-Only-For-Timing-1!");
            _ = adminPasswordHasher.VerifyHashedPassword(dummyAdmin, dummyHash, password);
            return;
        }

        var dummyStudent = new Student();
        var studentDummyHash = studentPasswordHasher.HashPassword(dummyStudent, "Dummy-Password-Only-For-Timing-1!");
        _ = studentPasswordHasher.VerifyHashedPassword(dummyStudent, studentDummyHash, password);
    }

    private Uri CreateResetLink(string rawToken)
    {
        var baseUrl = configuration["Web:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var webBaseUri)
            || (webBaseUri.Scheme != Uri.UriSchemeHttps && webBaseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("Web:BaseUrl yapılandırması geçersiz.");
        }

        var resetUrl = new Uri(webBaseUri, "/Account/ResetPassword").ToString();
        return new Uri(QueryHelpers.AddQueryString(resetUrl, "token", rawToken));
    }

    private static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static ServiceResult<T> InvalidLogin<T>() =>
        ServiceResult<T>.Failure("Kullanıcı adı veya parola hatalı.", StatusCodes.Status401Unauthorized);

    private static ServiceResult<PasswordResetResponse> InvalidPasswordReset() =>
        ServiceResult<PasswordResetResponse>.Failure(
            "Parola sıfırlama bağlantısı geçersiz veya süresi dolmuş.",
            StatusCodes.Status400BadRequest);
}
