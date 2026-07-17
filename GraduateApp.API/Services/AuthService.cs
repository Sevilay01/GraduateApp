using System.Security.Cryptography;
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
    Task<ServiceResult> ResetPasswordAsync(ResetPasswordDto request, CancellationToken cancellationToken);
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
    StudentRegistrationValidator registrationValidator) : IAuthService
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
        var normalizedEmail = NormalizeEmail(normalized.Email);
        var exists = await dbContext.Students.AnyAsync(
            student => student.Tc == request.Tc
                || student.NormalizedEmail == normalizedEmail
                || student.Email.ToUpper() == normalizedEmail
                || student.Telephone == normalized.Telephone,
            cancellationToken);

        if (exists)
        {
            return ServiceResult.Failure("Bu bilgilerle kayıt oluşturulamıyor.", StatusCodes.Status409Conflict);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var student = new Student
        {
            Tc = request.Tc,
            StudentName = normalized.FirstName,
            StudentSurname = normalized.LastName,
            FatherName = normalized.FatherName,
            BirthDate = normalized.BirthDate,
            Email = normalized.Email,
            NormalizedEmail = normalizedEmail,
            Telephone = normalized.Telephone,
            SecurityStamp = NewSecurityStamp(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        student.PasswordHash = studentPasswordHasher.HashPassword(student, request.Password);

        dbContext.Students.Add(student);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult.Success(StatusCodes.Status201Created);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return ServiceResult.Failure("Bu bilgilerle kayıt oluşturulamıyor.", StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException)
        {
            logger.LogError(
                StudentRegistrationDatabaseFailure,
                "Student registration failed because the database operation could not be completed.");
            return ServiceResult.Failure(
                "Kayıt şu anda oluşturulamıyor. Lütfen daha sonra tekrar deneyin.",
                StatusCodes.Status500InternalServerError);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException
                && sqlException.Errors.Cast<SqlError>().Any(error => error.Number is 2601 or 2627))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<ServiceResult<LoginResponse>> LoginAsync(LoginDto request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var normalizedEmail = NormalizeEmail(username);
        var now = timeProvider.GetUtcNow();

        var student = await dbContext.Students.FirstOrDefaultAsync(
            item => item.Tc == username
                || item.NormalizedEmail == normalizedEmail
                || item.Email.ToUpper() == normalizedEmail,
            cancellationToken);

        if (student is not null)
        {
            if (student.LockoutEndUtc > now)
            {
                return InvalidLogin<LoginResponse>();
            }

            var verification = studentPasswordHasher.VerifyHashedPassword(student, student.PasswordHash, request.Password);
            if (verification == PasswordVerificationResult.Failed)
            {
                await RecordStudentFailureAsync(student, now, cancellationToken);
                return InvalidLogin<LoginResponse>();
            }

            student.AccessFailedCount = 0;
            student.LockoutEndUtc = null;
            student.NormalizedEmail = NormalizeEmail(student.Email);
            student.UpdatedAtUtc = now.UtcDateTime;
            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            {
                student.PasswordHash = studentPasswordHasher.HashPassword(student, request.Password);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            var displayName = $"{student.StudentName} {student.StudentSurname}".Trim();
            var issued = accessTokenService.Issue(student.Tc, ApiAuthenticationDefaults.StudentRole, displayName, student.SecurityStamp);
            return ServiceResult<LoginResponse>.Success(new LoginResponse(
                issued.Token,
                issued.ExpiresAtUtc,
                ApiAuthenticationDefaults.StudentRole,
                displayName,
                false));
        }

        var admin = await dbContext.Admins.FirstOrDefaultAsync(
            item => item.NormalizedEmail == normalizedEmail || item.Email.ToUpper() == normalizedEmail,
            cancellationToken);

        if (admin is null || admin.LockoutEndUtc > now)
        {
            PerformDummyPasswordVerification(request.Password);
            return InvalidLogin<LoginResponse>();
        }

        var adminVerification = adminPasswordHasher.VerifyHashedPassword(admin, admin.PasswordHash, request.Password);
        if (adminVerification == PasswordVerificationResult.Failed)
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
        admin.NormalizedEmail = NormalizeEmail(admin.Email);
        admin.UpdatedAtUtc = now.UtcDateTime;
        if (adminVerification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            admin.PasswordHash = adminPasswordHasher.HashPassword(admin, request.Password);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var adminToken = accessTokenService.Issue(
            admin.AdminId.ToString(),
            ApiAuthenticationDefaults.AdminRole,
            "Yönetici",
            admin.SecurityStamp);
        return ServiceResult<LoginResponse>.Success(new LoginResponse(
            adminToken.Token,
            adminToken.ExpiresAtUtc,
            ApiAuthenticationDefaults.AdminRole,
            "Yönetici",
            admin.MustChangePassword));
    }

    public async Task RequestPasswordResetAsync(ForgotPasswordDto request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var student = await dbContext.Students.FirstOrDefaultAsync(
            item => item.NormalizedEmail == normalizedEmail || item.Email.ToUpper() == normalizedEmail,
            cancellationToken);
        var admin = student is null
            ? await dbContext.Admins.FirstOrDefaultAsync(
                item => item.NormalizedEmail == normalizedEmail || item.Email.ToUpper() == normalizedEmail,
                cancellationToken)
            : null;

        if (student is null && admin is null)
        {
            PerformDummyPasswordVerification(Guid.NewGuid().ToString("N"));
            return;
        }

        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var token = new PasswordResetToken
        {
            Tc = student?.Tc,
            AdminId = admin?.AdminId,
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = now,
            ExpirationDate = now.Add(ResetTokenLifetime),
            IsUsed = false
        };

        var activeTokens = await dbContext.PasswordResetTokens
            .Where(item => !item.IsUsed
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
        catch (Exception exception)
        {
            logger.LogError(exception, "Password reset bildirimi gönderilemedi.");
        }
    }

    public async Task<ServiceResult> ResetPasswordAsync(ResetPasswordDto request, CancellationToken cancellationToken)
    {
        if (!PasswordPolicy.IsValid(request.NewPassword))
        {
            return ServiceResult.Failure(PasswordPolicy.ValidationMessage, StatusCodes.Status400BadRequest);
        }

        var tokenHash = HashToken(request.Token);
        var token = await dbContext.PasswordResetTokens
            .Include(item => item.TcNavigation)
            .Include(item => item.Admin)
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (token is null || token.IsUsed || token.ExpirationDate <= now)
        {
            return ServiceResult.Failure("Parola sıfırlama bağlantısı geçersiz veya süresi dolmuş.", StatusCodes.Status400BadRequest);
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
            return ServiceResult.Failure("Parola sıfırlama bağlantısı geçersiz veya süresi dolmuş.", StatusCodes.Status400BadRequest);
        }

        var relatedTokens = await dbContext.PasswordResetTokens
            .Where(item => !item.IsUsed
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
            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Failure("Parola sıfırlama bağlantısı daha önce kullanılmış.", StatusCodes.Status409Conflict);
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
            if (admin is null || adminPasswordHasher.VerifyHashedPassword(admin, admin.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
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

    private void PerformDummyPasswordVerification(string password)
    {
        var dummyStudent = new Student();
        var dummyHash = studentPasswordHasher.HashPassword(dummyStudent, "Dummy-Password-Only-For-Timing-1!");
        _ = studentPasswordHasher.VerifyHashedPassword(dummyStudent, dummyHash, password);
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

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    private static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static ServiceResult<T> InvalidLogin<T>() =>
        ServiceResult<T>.Failure("Kullanıcı adı veya parola hatalı.", StatusCodes.Status401Unauthorized);
}
