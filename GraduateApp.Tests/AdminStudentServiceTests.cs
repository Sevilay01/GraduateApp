using GraduateApp.API.Models;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace GraduateApp.Tests;

public sealed class AdminStudentServiceTests
{
    [Fact]
    public async Task Deactivate_sets_persistent_status_rotates_stamp_and_revokes_reset_tokens()
    {
        await using var db = TestDb.Create();
        var student = CreateStudent("10000000146", "student@example.test");
        var originalStamp = student.SecurityStamp;
        db.Students.Add(student);
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Tc = student.Tc,
            TokenHash = "token-hash",
            ExpirationDate = new DateTime(2026, 8, 1),
            IsUsed = false
        });
        await db.SaveChangesAsync();
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var tokenService = new AccessTokenService(new EphemeralDataProtectionProvider(), db, clock);
        var token = tokenService.Issue(
            student.Tc,
            ApiAuthenticationDefaults.StudentRole,
            "Test Öğrenci",
            originalStamp).Token;
        Assert.NotNull(await tokenService.ValidateAsync(token, CancellationToken.None));
        var service = new AdminStudentService(db, clock);

        var result = await service.DeactivateAsync(student.PublicId, adminId: 7, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(student.IsActive);
        Assert.NotEqual(originalStamp, student.SecurityStamp);
        Assert.Null(student.LockoutEndUtc);
        Assert.Null(await tokenService.ValidateAsync(token, CancellationToken.None));
        Assert.True(db.PasswordResetTokens.Single().IsUsed);
        var audit = Assert.Single(db.SecurityAuditLogs);
        Assert.Equal(7, audit.ActorAdminId);
        Assert.Equal("StudentDeactivated", audit.EventType);
        Assert.Equal(student.PublicId.ToString("D"), audit.TargetId);
    }

    [Fact]
    public async Task Repeated_deactivation_returns_friendly_conflict_without_duplicate_audit()
    {
        await using var db = TestDb.Create();
        var student = CreateStudent("10000000146", "student@example.test");
        student.IsActive = false;
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var service = new AdminStudentService(db, TimeProvider.System);

        var result = await service.DeactivateAsync(student.PublicId, adminId: 7, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal("Öğrenci zaten pasif.", result.Error);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task Activate_sets_status_rotates_stamp_and_writes_audit()
    {
        await using var db = TestDb.Create();
        var student = CreateStudent("10000000146", "student@example.test");
        student.IsActive = false;
        student.LockoutEndUtc = DateTimeOffset.UtcNow.AddMinutes(5);
        var originalStamp = student.SecurityStamp;
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var service = new AdminStudentService(db, TimeProvider.System);

        var result = await service.ActivateAsync(student.PublicId, adminId: 7, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(student.IsActive);
        Assert.NotEqual(originalStamp, student.SecurityStamp);
        Assert.NotNull(student.LockoutEndUtc);
        Assert.Equal("StudentActivated", Assert.Single(db.SecurityAuditLogs).EventType);
    }

    [Fact]
    public async Task List_uses_persistent_status_independently_from_temporary_lockout()
    {
        await using var db = TestDb.Create();
        var activeButLocked = CreateStudent("10000000146", "active@example.test");
        activeButLocked.LockoutEndUtc = DateTimeOffset.UtcNow.AddMinutes(10);
        var inactiveButUnlocked = CreateStudent("10000000214", "inactive@example.test");
        inactiveButUnlocked.IsActive = false;
        inactiveButUnlocked.LockoutEndUtc = null;
        db.Students.AddRange(activeButLocked, inactiveButUnlocked);
        await db.SaveChangesAsync();
        var service = new AdminStudentService(db, TimeProvider.System);

        var result = await service.GetAsync(null, page: 1, pageSize: 10, CancellationToken.None);

        Assert.True(result.Items.Single(item => item.PublicId == activeButLocked.PublicId).IsActive);
        Assert.False(result.Items.Single(item => item.PublicId == inactiveButUnlocked.PublicId).IsActive);
    }

    [Fact]
    public async Task List_supports_search_and_paging_without_returning_raw_tc()
    {
        await using var db = TestDb.Create();
        for (var index = 0; index < 12; index++)
        {
            db.Students.Add(CreateStudent($"10000000{index:D3}", $"student{index}@example.test"));
        }

        await db.SaveChangesAsync();
        var service = new AdminStudentService(
            db,
            new TestTimeProvider(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero)));

        var firstPage = await service.GetAsync(null, page: 1, pageSize: 10, CancellationToken.None);
        var search = await service.GetAsync("student11@", page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(12, firstPage.TotalCount);
        Assert.Equal(10, firstPage.Items.Count);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.All(firstPage.Items, item => Assert.DoesNotContain("10000000", item.MaskedTc, StringComparison.Ordinal));
        Assert.Single(search.Items);
        Assert.Equal("student11@example.test", search.Items[0].Email);
    }

    private static Student CreateStudent(string tc, string email)
    {
        var student = new Student
        {
            Tc = tc,
            PublicId = Guid.NewGuid(),
            StudentName = "Test",
            StudentSurname = "Öğrenci",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true
        };
        student.LoginIdentity = new LoginIdentity
        {
            NormalizedEmail = student.NormalizedEmail,
            AccountType = LoginAccountType.Student,
            Student = student
        };
        return student;
    }
}
