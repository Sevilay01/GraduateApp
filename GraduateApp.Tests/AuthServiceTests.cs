using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public void Login_validation_messages_are_explicitly_turkish_and_require_account_type()
    {
        var model = new LoginDto();
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        Assert.Contains(results, result => result.ErrorMessage == "Kullanıcı adı zorunludur.");
        Assert.Contains(results, result => result.ErrorMessage == "Parola zorunludur.");
        Assert.Contains(results, result => result.ErrorMessage == "Hesap türü zorunludur.");
        Assert.DoesNotContain(results, result => result.ErrorMessage?.Contains("required", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task StudentLogin_AcceptsValidPassword_AndRejectsInvalidPassword()
    {
        await using var db = TestDb.Create();
        var studentHasher = new PasswordHasher<Student>();
        var student = CreateStudent();
        student.PasswordHash = studentHasher.HashPassword(student, "Strong-Student-1!");
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var service = CreateService(db, studentHasher: studentHasher);

        var valid = await service.LoginAsync(new LoginDto
        {
            Username = student.Tc,
            Password = "Strong-Student-1!",
            AccountType = LoginAccountType.Student
        }, CancellationToken.None);
        var invalid = await service.LoginAsync(new LoginDto
        {
            Username = student.Tc,
            Password = "Wrong-Password-1!",
            AccountType = LoginAccountType.Student
        }, CancellationToken.None);

        Assert.True(valid.IsSuccess);
        Assert.Equal("Student", valid.Value!.Role);
        Assert.False(invalid.IsSuccess);
        Assert.Equal(StatusCodes.Status401Unauthorized, invalid.StatusCode);
    }

    [Fact]
    public async Task Inactive_student_cannot_login_with_correct_password_and_response_does_not_disclose_account()
    {
        await using var db = TestDb.Create();
        var studentHasher = new PasswordHasher<Student>();
        var student = CreateStudent();
        student.IsActive = false;
        student.PasswordHash = studentHasher.HashPassword(student, "Strong-Student-1!");
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var service = CreateService(db, studentHasher: studentHasher);

        var inactive = await service.LoginAsync(new LoginDto
        {
            Username = student.Email,
            Password = "Strong-Student-1!",
            AccountType = LoginAccountType.Student
        }, CancellationToken.None);
        var missing = await service.LoginAsync(new LoginDto
        {
            Username = "missing@example.test",
            Password = "Strong-Student-1!",
            AccountType = LoginAccountType.Student
        }, CancellationToken.None);

        Assert.False(inactive.IsSuccess);
        Assert.Equal(StatusCodes.Status401Unauthorized, inactive.StatusCode);
        Assert.Equal(missing.Error, inactive.Error);
        Assert.DoesNotContain(student.Email, inactive.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(student.Tc, inactive.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forgot_password_does_not_create_or_send_token_for_inactive_student()
    {
        await using var db = TestDb.Create();
        var student = CreateStudent();
        student.IsActive = false;
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var email = new CapturingEmailSender();
        var service = CreateService(db, emailSender: email);

        await service.RequestPasswordResetAsync(
            new ForgotPasswordDto { Email = student.Email },
            CancellationToken.None);

        Assert.Null(email.ResetLink);
        Assert.Empty(db.PasswordResetTokens);
    }

    [Fact]
    public async Task Password_reset_cannot_reactivate_an_inactive_student()
    {
        await using var db = TestDb.Create();
        var studentHasher = new PasswordHasher<Student>();
        var student = CreateStudent();
        student.PasswordHash = studentHasher.HashPassword(student, "Strong-Student-1!");
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var email = new CapturingEmailSender();
        var service = CreateService(db, emailSender: email, studentHasher: studentHasher);
        await service.RequestPasswordResetAsync(
            new ForgotPasswordDto { Email = student.Email },
            CancellationToken.None);
        var token = ExtractToken(email.ResetLink!);
        student.IsActive = false;
        await db.SaveChangesAsync();

        var result = await service.ResetPasswordAsync(new ResetPasswordDto
        {
            Token = token,
            NewPassword = "New-Student-Password-1!",
            ConfirmPassword = "New-Student-Password-1!"
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(student.IsActive);
        Assert.Equal(
            PasswordVerificationResult.Success,
            studentHasher.VerifyHashedPassword(student, student.PasswordHash, "Strong-Student-1!"));
    }

    [Fact]
    public async Task Temporary_lockout_expires_without_changing_persistent_active_status()
    {
        await using var db = TestDb.Create();
        var studentHasher = new PasswordHasher<Student>();
        var student = CreateStudent();
        student.LockoutEndUtc = new DateTimeOffset(2026, 7, 20, 9, 5, 0, TimeSpan.Zero);
        student.PasswordHash = studentHasher.HashPassword(student, "Strong-Student-1!");
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero));
        var service = CreateService(db, clock, studentHasher: studentHasher);

        var locked = await service.LoginAsync(new LoginDto
        {
            Username = student.Tc,
            Password = "Strong-Student-1!",
            AccountType = LoginAccountType.Student
        }, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(6));
        var afterLockout = await service.LoginAsync(new LoginDto
        {
            Username = student.Tc,
            Password = "Strong-Student-1!",
            AccountType = LoginAccountType.Student
        }, CancellationToken.None);

        Assert.False(locked.IsSuccess);
        Assert.True(afterLockout.IsSuccess);
        Assert.True(student.IsActive);
    }

    [Fact]
    public async Task AdminLogin_AcceptsValidPassword_AndRejectsInvalidPassword()
    {
        await using var db = TestDb.Create();
        var adminHasher = new PasswordHasher<Admin>();
        var admin = CreateAdmin();
        admin.PasswordHash = adminHasher.HashPassword(admin, "Strong-Admin-1!");
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        var service = CreateService(db, adminHasher: adminHasher);

        var valid = await service.LoginAsync(new LoginDto
        {
            Username = admin.Email,
            Password = "Strong-Admin-1!",
            AccountType = LoginAccountType.Admin
        }, CancellationToken.None);
        var invalid = await service.LoginAsync(new LoginDto
        {
            Username = admin.Email,
            Password = "Wrong-Admin-1!",
            AccountType = LoginAccountType.Admin
        }, CancellationToken.None);

        Assert.True(valid.IsSuccess);
        Assert.Equal("Admin", valid.Value!.Role);
        Assert.False(invalid.IsSuccess);
        Assert.Equal(StatusCodes.Status401Unauthorized, invalid.StatusCode);
        Assert.Equal(1, admin.AccessFailedCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Inactive_or_pending_admin_cannot_login_and_failed_count_is_unchanged(
        bool isActive,
        bool isInvitationPending)
    {
        await using var db = TestDb.Create();
        var hasher = new PasswordHasher<Admin>();
        var admin = CreateAdmin();
        admin.IsActive = isActive;
        admin.IsInvitationPending = isInvitationPending;
        admin.PasswordHash = hasher.HashPassword(admin, "Strong-Admin-1!");
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        var service = CreateService(db, adminHasher: hasher);

        var result = await service.LoginAsync(new LoginDto
        {
            Username = admin.Email,
            Password = "Strong-Admin-1!",
            AccountType = LoginAccountType.Admin
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        Assert.Equal("Kullanıcı adı veya parola hatalı.", result.Error);
        Assert.Equal(0, admin.AccessFailedCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Inactive_or_pending_admin_does_not_receive_normal_password_reset(
        bool isActive,
        bool isInvitationPending)
    {
        await using var db = TestDb.Create();
        var admin = CreateAdmin();
        admin.IsActive = isActive;
        admin.IsInvitationPending = isInvitationPending;
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        var sender = new CapturingEmailSender();
        var service = CreateService(db, emailSender: sender);

        await service.RequestPasswordResetAsync(new ForgotPasswordDto { Email = admin.Email }, CancellationToken.None);

        Assert.Null(sender.ResetLink);
        Assert.Empty(db.PasswordResetTokens);
    }

    [Fact]
    public async Task Invitation_token_is_not_accepted_by_normal_password_reset()
    {
        await using var db = TestDb.Create();
        var admin = CreateAdmin();
        admin.IsInvitationPending = true;
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        const string rawToken = "invitation-purpose-token";
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            AdminId = admin.AdminId,
            Purpose = PasswordResetTokenPurpose.AdminInvitation,
            TokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken))),
            CreatedAtUtc = DateTime.UtcNow,
            ExpirationDate = DateTime.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.ResetPasswordAsync(new ResetPasswordDto
        {
            Token = rawToken,
            NewPassword = "Strong-New-Admin-1!",
            ConfirmPassword = "Strong-New-Admin-1!"
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(admin.IsInvitationPending);
    }

    [Fact]
    public async Task Missing_admin_identity_does_not_verify_a_real_admin_or_increment_its_counter()
    {
        await using var db = TestDb.Create();
        var admin = CreateAdmin();
        var trackingHasher = new TrackingAdminPasswordHasher();
        admin.PasswordHash = trackingHasher.HashPassword(admin, "Strong-Admin-1!");
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        var service = CreateService(db, adminHasher: trackingHasher);

        var result = await service.LoginAsync(new LoginDto
        {
            Username = "missing@example.test",
            Password = "Wrong-Admin-1!",
            AccountType = LoginAccountType.Admin
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(admin, trackingHasher.VerifiedUsers);
        Assert.Equal(0, admin.AccessFailedCount);
    }

    [Fact]
    public async Task Ordered_login_and_password_reset_queries_do_not_emit_row_limiting_warning()
    {
        await using var db = TestDb.CreateWithStrictQueryWarnings();
        var service = CreateService(db);

        var login = await service.LoginAsync(new LoginDto
        {
            Username = "missing@example.test",
            Password = "Wrong-Admin-1!",
            AccountType = LoginAccountType.Admin
        }, CancellationToken.None);
        await service.RequestPasswordResetAsync(
            new ForgotPasswordDto { Email = "missing@example.test" },
            CancellationToken.None);

        Assert.False(login.IsSuccess);
    }

    [Fact]
    public async Task PasswordReset_RejectsExpiredAndReusedTokens()
    {
        await using var db = TestDb.Create();
        var studentHasher = new PasswordHasher<Student>();
        var student = CreateStudent();
        student.PasswordHash = studentHasher.HashPassword(student, "Strong-Student-1!");
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero));
        var email = new CapturingEmailSender();
        var service = CreateService(db, clock, email, studentHasher);

        await service.RequestPasswordResetAsync(new ForgotPasswordDto { Email = student.Email }, CancellationToken.None);
        var expiredToken = ExtractToken(email.ResetLink!);
        clock.Advance(TimeSpan.FromMinutes(31));
        var expired = await service.ResetPasswordAsync(new ResetPasswordDto
        {
            Token = expiredToken,
            NewPassword = "New-Student-Password-1!",
            ConfirmPassword = "New-Student-Password-1!"
        }, CancellationToken.None);
        Assert.False(expired.IsSuccess);

        await service.RequestPasswordResetAsync(new ForgotPasswordDto { Email = student.Email }, CancellationToken.None);
        var validToken = ExtractToken(email.ResetLink!);
        var request = new ResetPasswordDto
        {
            Token = validToken,
            NewPassword = "Another-Password-1!",
            ConfirmPassword = "Another-Password-1!"
        };
        var firstUse = await service.ResetPasswordAsync(request, CancellationToken.None);
        var secondUse = await service.ResetPasswordAsync(request, CancellationToken.None);

        Assert.True(firstUse.IsSuccess);
        Assert.Equal(LoginAccountType.Student, firstUse.Value!.AccountType);
        Assert.False(secondUse.IsSuccess);
        Assert.Null(secondUse.Value);
    }

    [Fact]
    public async Task Logout_ChangesSecurityStampAndRevokesExistingSessions()
    {
        await using var db = TestDb.Create();
        var student = CreateStudent();
        student.PasswordHash = new PasswordHasher<Student>().HashPassword(student, "Strong-Student-1!");
        var previousStamp = student.SecurityStamp;
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.RevokeSessionsAsync(student.Tc, "Student", CancellationToken.None);

        Assert.NotEqual(previousStamp, student.SecurityStamp);
    }

    [Fact]
    public async Task RegisterStudent_AllowsStudentsWithRequiredFields_AndHashesPasswords()
    {
        await using var db = TestDb.Create();
        var hasher = new PasswordHasher<Student>();
        var service = CreateService(db, studentHasher: hasher);
        var firstRequest = CreateRegistrationRequest(
            "10000000078",
            "first.student@example.test",
            "First-Student-Password-1!");
        var secondRequest = CreateRegistrationRequest(
            "10000000214",
            "second.student@example.test",
            "Second-Student-Password-1!");

        var first = await service.RegisterStudentAsync(firstRequest, CancellationToken.None);
        var second = await service.RegisterStudentAsync(secondRequest, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(StatusCodes.Status201Created, first.StatusCode);
        Assert.True(second.IsSuccess);
        Assert.Equal(StatusCodes.Status201Created, second.StatusCode);
        var students = db.Students.OrderBy(student => student.Tc).ToArray();
        Assert.Equal(2, students.Length);
        Assert.All(students, student => Assert.True(student.IsActive));
        Assert.Equal(2, students.Select(student => student.PublicId).Distinct().Count());
        Assert.DoesNotContain(students, student => student.PublicId == Guid.Empty);
        Assert.All(students, student => Assert.StartsWith("+905", student.Telephone, StringComparison.Ordinal));
        var storedFirst = students.Single(student => student.Tc == firstRequest.Tc);
        Assert.Equal("Test Baba", storedFirst.FatherName);
        Assert.Equal(new DateOnly(2000, 1, 1), storedFirst.BirthDate);
        Assert.Equal(firstRequest.Email.ToUpperInvariant(), storedFirst.NormalizedEmail);
        Assert.DoesNotContain(students, student => student.PasswordHash == firstRequest.Password);
        Assert.DoesNotContain(students, student => student.PasswordHash == secondRequest.Password);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(students.Single(student => student.Tc == firstRequest.Tc), students.Single(student => student.Tc == firstRequest.Tc).PasswordHash, firstRequest.Password));
    }

    [Fact]
    public async Task RegisterStudent_rejects_same_canonical_telephone_in_another_format()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);
        var first = CreateRegistrationRequest(
            "10000000078",
            "first@example.test",
            "First-Student-Password-1!",
            "0 (532) 123-45-67");
        var second = CreateRegistrationRequest(
            "10000000214",
            "second@example.test",
            "Second-Student-Password-1!",
            "+905321234567");

        var created = await service.RegisterStudentAsync(first, CancellationToken.None);
        var duplicate = await service.RegisterStudentAsync(second, CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, duplicate.StatusCode);
        Assert.Equal("+905321234567", db.Students.Single().Telephone);
        Assert.DoesNotContain(second.Telephone, duplicate.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterStudent_ReturnsSafeConflictForDuplicateTcOrEmail()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);
        var original = CreateRegistrationRequest("10000000078", "student@example.test", "Original-Password-1!");
        var created = await service.RegisterStudentAsync(original, CancellationToken.None);

        var duplicateTc = await service.RegisterStudentAsync(
            CreateRegistrationRequest(original.Tc, "different@example.test", "Different-Password-1!"),
            CancellationToken.None);
        var duplicateEmail = await service.RegisterStudentAsync(
            CreateRegistrationRequest("10000000214", "STUDENT@example.test", "Another-Password-1!"),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.False(duplicateTc.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, duplicateTc.StatusCode);
        Assert.Equal("Bu bilgilerle kayıt oluşturulamıyor.", duplicateTc.Error);
        Assert.False(duplicateEmail.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, duplicateEmail.StatusCode);
        Assert.Equal("Bu bilgilerle kayıt oluşturulamıyor.", duplicateEmail.Error);
        Assert.DoesNotContain(original.Tc, duplicateTc.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(original.Email, duplicateEmail.Error, StringComparison.OrdinalIgnoreCase);
    }

    [LocalDbTheory]
    [InlineData(2601)]
    [InlineData(2627)]
    public async Task RegisterStudent_MapsOnlySqlServerUniqueViolationsToSafeConflict(int errorNumber)
    {
        var sqlException = await LocalDbTestSupport.CreateUniqueViolationExceptionAsync(errorNumber);
        var interceptor = new ThrowingSaveChangesInterceptor(
            () => new DbUpdateException("Database persistence failed.", sqlException));
        await using var db = TestDb.Create(interceptor);
        var logger = new CapturingLogger<AuthService>();
        var service = CreateService(db, logger: logger);
        var request = CreateRegistrationRequest(
            "10000000078",
            "private.student@example.test",
            "Private-Student-Password-1!");

        var result = await service.RegisterStudentAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal("Bu bilgilerle kayıt oluşturulamıyor.", result.Error);
        Assert.Empty(logger.Entries);
        Assert.DoesNotContain(request.Tc, result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(request.Email, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterStudent_ReturnsControlledErrorAndPiiFreeLogForUnexpectedDatabaseFailure()
    {
        const string telephone = "555000000000001";
        var request = CreateRegistrationRequest(
            "10000000078",
            "private.student@example.test",
            "Private-Student-Password-1!");
        var interceptor = new ThrowingSaveChangesInterceptor(
            () => new DbUpdateException(
                $"TC={request.Tc}; Email={request.Email}; Telephone={telephone}; Father={request.FatherName}; BirthDate={request.BirthDate}"));
        await using var db = TestDb.Create(interceptor);
        var logger = new CapturingLogger<AuthService>();
        var service = CreateService(db, logger: logger);

        var result = await service.RegisterStudentAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.Equal("Kayıt şu anda oluşturulamıyor. Lütfen daha sonra tekrar deneyin.", result.Error);
        Assert.Single(logger.Entries);
        Assert.Contains("database operation could not be completed", logger.Entries[0], StringComparison.Ordinal);
        var output = string.Join(Environment.NewLine, logger.Entries.Append(result.Error));
        Assert.DoesNotContain(request.Tc, output, StringComparison.Ordinal);
        Assert.DoesNotContain(request.Email, output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(telephone, output, StringComparison.Ordinal);
        Assert.DoesNotContain(request.FatherName, output, StringComparison.Ordinal);
        Assert.DoesNotContain(request.BirthDate.ToString("O"), output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterStudent_rejects_admin_email_with_invariant_case_and_whitespace_normalization()
    {
        await using var db = TestDb.Create();
        var admin = CreateAdmin();
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var request = CreateRegistrationRequest(
            "10000000078",
            "  AdMiN@Example.Test  ",
            "Student-Password-1!");

        var result = await service.RegisterStudentAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Single(db.Admins);
        Assert.Empty(db.Students);
        Assert.Single(db.LoginIdentities);
        Assert.DoesNotContain(admin.Email, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Explicit_account_type_prevents_cross_role_login_fallback()
    {
        await using var db = TestDb.Create();
        var studentHasher = new PasswordHasher<Student>();
        var adminHasher = new PasswordHasher<Admin>();
        var student = CreateStudent();
        var admin = CreateAdmin();
        student.PasswordHash = studentHasher.HashPassword(student, "Strong-Student-1!");
        admin.PasswordHash = adminHasher.HashPassword(admin, "Strong-Admin-1!");
        db.AddRange(student, admin);
        await db.SaveChangesAsync();
        var service = CreateService(db, studentHasher: studentHasher, adminHasher: adminHasher);

        var studentRouteWithAdminCredentials = await service.LoginAsync(new LoginDto
        {
            Username = admin.Email,
            Password = "Strong-Admin-1!",
            AccountType = LoginAccountType.Student
        }, CancellationToken.None);
        var adminRouteWithStudentCredentials = await service.LoginAsync(new LoginDto
        {
            Username = student.Email,
            Password = "Strong-Student-1!",
            AccountType = LoginAccountType.Admin
        }, CancellationToken.None);
        var adminLogin = await service.LoginAsync(new LoginDto
        {
            Username = admin.Email,
            Password = "Strong-Admin-1!",
            AccountType = LoginAccountType.Admin
        }, CancellationToken.None);

        Assert.False(studentRouteWithAdminCredentials.IsSuccess);
        Assert.False(adminRouteWithStudentCredentials.IsSuccess);
        Assert.True(adminLogin.IsSuccess);
        Assert.Equal(ApiAuthenticationDefaults.AdminRole, adminLogin.Value!.Role);
    }

    [Fact]
    public async Task Account_type_filter_resolves_only_requested_role_when_corrupt_data_has_same_email()
    {
        await using var db = TestDb.Create();
        var studentHasher = new PasswordHasher<Student>();
        var adminHasher = new PasswordHasher<Admin>();
        var student = CreateStudent();
        var admin = CreateAdmin();
        student.Email = admin.Email;
        student.NormalizedEmail = admin.NormalizedEmail;
        student.LoginIdentity!.NormalizedEmail = admin.NormalizedEmail;
        student.PasswordHash = studentHasher.HashPassword(student, "Strong-Student-1!");
        admin.PasswordHash = adminHasher.HashPassword(admin, "Strong-Admin-1!");
        db.AddRange(student, admin);
        await db.SaveChangesAsync();
        var service = CreateService(db, studentHasher: studentHasher, adminHasher: adminHasher);

        var adminLogin = await service.LoginAsync(new LoginDto
        {
            Username = admin.Email,
            Password = "Strong-Admin-1!",
            AccountType = LoginAccountType.Admin
        }, CancellationToken.None);
        var studentLogin = await service.LoginAsync(new LoginDto
        {
            Username = student.Email,
            Password = "Strong-Student-1!",
            AccountType = LoginAccountType.Student
        }, CancellationToken.None);

        Assert.True(adminLogin.IsSuccess);
        Assert.Equal(ApiAuthenticationDefaults.AdminRole, adminLogin.Value!.Role);
        Assert.True(studentLogin.IsSuccess);
        Assert.Equal(ApiAuthenticationDefaults.StudentRole, studentLogin.Value!.Role);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task Numeric_admin_email_cannot_collide_with_student_tc_login_query()
    {
        await using var db = TestDb.Create();
        var studentHasher = new PasswordHasher<Student>();
        var student = CreateStudent();
        var admin = CreateAdmin();
        admin.Email = student.Tc;
        admin.NormalizedEmail = student.Tc;
        admin.LoginIdentity!.NormalizedEmail = student.Tc;
        student.PasswordHash = studentHasher.HashPassword(student, "Strong-Student-1!");
        admin.PasswordHash = new PasswordHasher<Admin>().HashPassword(admin, "Strong-Admin-1!");
        db.AddRange(student, admin);
        await db.SaveChangesAsync();
        var service = CreateService(db, studentHasher: studentHasher);

        var result = await service.LoginAsync(new LoginDto
        {
            Username = student.Tc,
            Password = "Strong-Student-1!",
            AccountType = LoginAccountType.Student
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApiAuthenticationDefaults.StudentRole, result.Value!.Role);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task Forgot_password_resolves_exact_identity_and_binds_token_to_admin()
    {
        await using var db = TestDb.Create();
        var admin = CreateAdmin();
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        var email = new CapturingEmailSender();
        var service = CreateService(db, emailSender: email);

        await service.RequestPasswordResetAsync(
            new ForgotPasswordDto { Email = "  ADMIN@example.test " },
            CancellationToken.None);

        var token = Assert.Single(db.PasswordResetTokens);
        Assert.Equal(admin.AdminId, token.AdminId);
        Assert.Null(token.Tc);
        Assert.Equal(PasswordResetTokenPurpose.PasswordReset, token.Purpose);
        Assert.NotNull(email.ResetLink);
    }

    [Fact]
    public async Task Admin_password_reset_enables_only_new_password_for_admin_login()
    {
        await using var db = TestDb.Create();
        var adminHasher = new PasswordHasher<Admin>();
        var admin = CreateAdmin();
        admin.PasswordHash = adminHasher.HashPassword(admin, "Old-Admin-Password-1!");
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        var email = new CapturingEmailSender();
        var service = CreateService(db, emailSender: email, adminHasher: adminHasher);
        await service.RequestPasswordResetAsync(
            new ForgotPasswordDto { Email = admin.Email },
            CancellationToken.None);

        var reset = await service.ResetPasswordAsync(new ResetPasswordDto
        {
            Token = ExtractToken(email.ResetLink!),
            NewPassword = "New-Admin-Password-1!",
            ConfirmPassword = "New-Admin-Password-1!"
        }, CancellationToken.None);
        var oldPassword = await service.LoginAsync(new LoginDto
        {
            Username = admin.Email,
            Password = "Old-Admin-Password-1!",
            AccountType = LoginAccountType.Admin
        }, CancellationToken.None);
        var newPassword = await service.LoginAsync(new LoginDto
        {
            Username = admin.Email,
            Password = "New-Admin-Password-1!",
            AccountType = LoginAccountType.Admin
        }, CancellationToken.None);

        Assert.True(reset.IsSuccess);
        Assert.Equal(LoginAccountType.Admin, reset.Value!.AccountType);
        Assert.False(oldPassword.IsSuccess);
        Assert.True(newPassword.IsSuccess);
        Assert.False(admin.MustChangePassword);
    }

    [Fact]
    public async Task Invalid_reset_token_does_not_return_account_type()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);

        var result = await service.ResetPasswordAsync(new ResetPasswordDto
        {
            Token = "invalid-token",
            NewPassword = "New-Password-1!",
            ConfirmPassword = "New-Password-1!"
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("Parola sıfırlama bağlantısı geçersiz veya süresi dolmuş.", result.Error);
    }

    [Fact]
    public async Task Corrupt_identity_returns_generic_unauthorized_and_writes_pii_free_audit()
    {
        await using var db = TestDb.Create();
        var student = CreateStudent();
        student.LoginIdentity!.NormalizedEmail = "MISMATCH@EXAMPLE.TEST";
        student.PasswordHash = new PasswordHasher<Student>()
            .HashPassword(student, "Strong-Student-1!");
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var logger = new CapturingLogger<AuthService>();
        var service = CreateService(db, logger: logger);

        var result = await service.LoginAsync(new LoginDto
        {
            Username = student.Tc,
            Password = "Strong-Student-1!",
            AccountType = LoginAccountType.Student
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        Assert.Equal("Kullanıcı adı veya parola hatalı.", result.Error);
        var audit = Assert.Single(db.SecurityAuditLogs);
        Assert.Equal("LoginIdentityIntegrityFailure", audit.EventType);
        var output = string.Join(Environment.NewLine, logger.Entries.Append(audit.Details));
        Assert.DoesNotContain(student.Tc, output, StringComparison.Ordinal);
        Assert.DoesNotContain(student.Email, output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Strong-Student", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Corrupt_admin_identity_returns_safe_unauthorized_without_password_attempt()
    {
        await using var db = TestDb.Create();
        var admin = CreateAdmin();
        admin.NormalizedEmail = "MISMATCH@EXAMPLE.TEST";
        admin.PasswordHash = new PasswordHasher<Admin>()
            .HashPassword(admin, "Strong-Admin-1!");
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        var logger = new CapturingLogger<AuthService>();
        var service = CreateService(db, logger: logger);

        var result = await service.LoginAsync(new LoginDto
        {
            Username = admin.Email,
            Password = "Strong-Admin-1!",
            AccountType = LoginAccountType.Admin
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        Assert.Equal(0, admin.AccessFailedCount);
        var audit = Assert.Single(db.SecurityAuditLogs);
        Assert.Equal("AdminIdentityIntegrityFailure", audit.EventType);
        Assert.Equal("Admin", audit.TargetType);
        Assert.Equal(admin.PublicId.ToString("D"), audit.TargetId);
        var output = string.Join(Environment.NewLine, logger.Entries.Append(audit.Details));
        Assert.DoesNotContain(admin.Email, output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Strong-Admin", output, StringComparison.Ordinal);
    }

    private static AuthService CreateService(
        GraduateAppDbContext db,
        TestTimeProvider? timeProvider = null,
        CapturingEmailSender? emailSender = null,
        IPasswordHasher<Student>? studentHasher = null,
        IPasswordHasher<Admin>? adminHasher = null,
        ILogger<AuthService>? logger = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Web:BaseUrl"] = "https://localhost:7272"
            })
            .Build();
        var clock = timeProvider ?? new TestTimeProvider(DateTimeOffset.UtcNow);
        return new AuthService(
            db,
            studentHasher ?? new PasswordHasher<Student>(),
            adminHasher ?? new PasswordHasher<Admin>(),
            new StubAccessTokenService(),
            emailSender ?? new CapturingEmailSender(),
            configuration,
            clock,
            logger ?? NullLogger<AuthService>.Instance,
            new StudentRegistrationValidator(clock, Options.Create(new RegistrationOptions())),
            new InvariantEmailNormalizer());
    }

    private static RegisterStudentDto CreateRegistrationRequest(
        string tc,
        string email,
        string password,
        string? telephone = null) => new()
        {
            Tc = tc,
            FirstName = "Test",
            LastName = "Öğrenci",
            FatherName = "Test Baba",
            BirthDate = new DateOnly(2000, 1, 1),
            Email = email,
            Telephone = telephone ?? $"05{tc[^9..]}",
            Password = password,
            ConfirmPassword = password
        };

    private static Student CreateStudent()
    {
        var student = new Student
        {
            Tc = "10000000146",
            PublicId = Guid.NewGuid(),
            StudentName = "Test",
            StudentSurname = "Öğrenci",
            Email = "student@example.test",
            NormalizedEmail = "STUDENT@EXAMPLE.TEST",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        student.LoginIdentity = new LoginIdentity
        {
            NormalizedEmail = student.NormalizedEmail,
            AccountType = LoginAccountType.Student,
            Student = student,
            CreatedAtUtc = DateTime.UtcNow
        };
        return student;
    }

    private static Admin CreateAdmin()
    {
        var admin = new Admin
        {
            Email = "admin@example.test",
            NormalizedEmail = "ADMIN@EXAMPLE.TEST",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        admin.LoginIdentity = new LoginIdentity
        {
            NormalizedEmail = admin.NormalizedEmail,
            AccountType = LoginAccountType.Admin,
            Admin = admin,
            CreatedAtUtc = DateTime.UtcNow
        };
        return admin;
    }

    private static string ExtractToken(Uri resetLink) =>
        QueryHelpers.ParseQuery(resetLink.Query)["token"].ToString();

    private sealed class TrackingAdminPasswordHasher : IPasswordHasher<Admin>
    {
        private readonly PasswordHasher<Admin> inner = new();

        public List<Admin> VerifiedUsers { get; } = [];

        public string HashPassword(Admin user, string password) => inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(
            Admin user,
            string hashedPassword,
            string providedPassword)
        {
            VerifiedUsers.Add(user);
            return inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
