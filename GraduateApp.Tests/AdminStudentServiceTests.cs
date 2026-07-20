using GraduateApp.API.Models;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.DataProtection;

namespace GraduateApp.Tests;

public sealed class AdminStudentServiceTests
{
    [Fact]
    public async Task Deactivate_rotates_security_stamp_locks_account_and_revokes_reset_tokens()
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
        var token = tokenService.Issue(student.Tc, ApiAuthenticationDefaults.StudentRole, "Test Öğrenci", originalStamp).Token;
        Assert.NotNull(await tokenService.ValidateAsync(token, CancellationToken.None));
        var service = new AdminStudentService(db, clock);

        var result = await service.DeactivateAsync(student.Tc, adminId: 7, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(originalStamp, student.SecurityStamp);
        Assert.Equal(DateTimeOffset.MaxValue, student.LockoutEndUtc);
        Assert.Null(await tokenService.ValidateAsync(token, CancellationToken.None));
        Assert.True(db.PasswordResetTokens.Single().IsUsed);
        var audit = Assert.Single(db.SecurityAuditLogs);
        Assert.Equal(7, audit.ActorAdminId);
        Assert.Equal("StudentDeactivated", audit.EventType);
    }

    [Fact]
    public async Task List_supports_search_and_paging()
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
        Assert.Single(search.Items);
        Assert.Equal("student11@example.test", search.Items[0].Email);
    }

    private static Student CreateStudent(string tc, string email) => new()
    {
        Tc = tc,
        StudentName = "Test",
        StudentSurname = "Öğrenci",
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        PasswordHash = "hash",
        SecurityStamp = Guid.NewGuid().ToString("N")
    };
}
