using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GraduateApp.Tests;

public sealed class AuthServiceTests
{
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
            Password = "Strong-Student-1!"
        }, CancellationToken.None);
        var invalid = await service.LoginAsync(new LoginDto
        {
            Username = student.Tc,
            Password = "Wrong-Password-1!"
        }, CancellationToken.None);

        Assert.True(valid.IsSuccess);
        Assert.Equal("Student", valid.Value!.Role);
        Assert.False(invalid.IsSuccess);
        Assert.Equal(StatusCodes.Status401Unauthorized, invalid.StatusCode);
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
            Password = "Strong-Admin-1!"
        }, CancellationToken.None);
        var invalid = await service.LoginAsync(new LoginDto
        {
            Username = admin.Email,
            Password = "Wrong-Admin-1!"
        }, CancellationToken.None);

        Assert.True(valid.IsSuccess);
        Assert.Equal("Admin", valid.Value!.Role);
        Assert.False(invalid.IsSuccess);
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
        Assert.False(secondUse.IsSuccess);
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

    private static AuthService CreateService(
        GraduateAppDbContext db,
        TestTimeProvider? timeProvider = null,
        CapturingEmailSender? emailSender = null,
        PasswordHasher<Student>? studentHasher = null,
        PasswordHasher<Admin>? adminHasher = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Web:BaseUrl"] = "https://localhost:7272"
            })
            .Build();
        return new AuthService(
            db,
            studentHasher ?? new PasswordHasher<Student>(),
            adminHasher ?? new PasswordHasher<Admin>(),
            new StubAccessTokenService(),
            emailSender ?? new CapturingEmailSender(),
            configuration,
            timeProvider ?? new TestTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<AuthService>.Instance);
    }

    private static Student CreateStudent() => new()
    {
        Tc = "10000000146",
        StudentName = "Test",
        StudentSurname = "Öğrenci",
        Email = "student@example.test",
        NormalizedEmail = "STUDENT@EXAMPLE.TEST",
        SecurityStamp = Guid.NewGuid().ToString("N"),
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Admin CreateAdmin() => new()
    {
        Email = "admin@example.test",
        NormalizedEmail = "ADMIN@EXAMPLE.TEST",
        SecurityStamp = Guid.NewGuid().ToString("N"),
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static string ExtractToken(Uri resetLink) =>
        QueryHelpers.ParseQuery(resetLink.Query)["token"].ToString();
}
