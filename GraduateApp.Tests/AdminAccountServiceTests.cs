using System.Security.Cryptography;
using System.Text;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed class AdminAccountServiceTests
{
    private static readonly byte[] RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public async Task Invite_creates_admin_identity_and_hashed_purpose_bound_token_in_one_unit()
    {
        await using var db = TestDb.Create();
        var actor = CreateAdmin("actor@example.test", RowVersion);
        db.Admins.Add(actor);
        await db.SaveChangesAsync();
        var sender = new CapturingInvitationEmailSender();
        var logger = new CapturingLogger<AdminAccountService>();
        var service = CreateService(db, sender, logger);

        var result = await service.InviteAsync(
            actor.AdminId,
            new InviteAdminDto { Email = "  invited@example.test  " },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var invited = await db.Admins.Include(item => item.LoginIdentity)
            .SingleAsync(item => item.NormalizedEmail == "INVITED@EXAMPLE.TEST");
        Assert.True(invited.IsActive);
        Assert.True(invited.IsInvitationPending);
        Assert.NotEqual(Guid.Empty, invited.PublicId);
        Assert.NotNull(invited.LoginIdentity);
        Assert.Equal(LoginAccountType.Admin, invited.LoginIdentity!.AccountType);
        var token = await db.PasswordResetTokens.SingleAsync(item => item.AdminId == invited.AdminId);
        Assert.Equal(PasswordResetTokenPurpose.AdminInvitation, token.Purpose);
        var rawToken = QueryHelpers.ParseQuery(sender.InvitationLink!.Query)["token"].ToString();
        Assert.NotEmpty(rawToken);
        Assert.NotEqual(rawToken, token.TokenHash);
        Assert.Equal(HashToken(rawToken), token.TokenHash);
        var output = string.Join(Environment.NewLine, logger.Entries);
        Assert.DoesNotContain(rawToken, output, StringComparison.Ordinal);
        Assert.DoesNotContain("invited@example.test", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invite_rejects_student_or_existing_admin_email()
    {
        await using var db = TestDb.Create();
        var actor = CreateAdmin("actor@example.test", RowVersion);
        var student = CreateStudent("student@example.test");
        db.AddRange(actor, student);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var studentCollision = await service.InviteAsync(actor.AdminId, new InviteAdminDto { Email = student.Email }, CancellationToken.None);
        var adminCollision = await service.InviteAsync(actor.AdminId, new InviteAdminDto { Email = actor.Email }, CancellationToken.None);

        Assert.False(studentCollision.IsSuccess);
        Assert.False(adminCollision.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, studentCollision.StatusCode);
        Assert.Equal(1, await db.Admins.CountAsync());
    }

    [Fact]
    public async Task Resend_invalidates_old_invitation_and_accept_sets_strong_password_once()
    {
        await using var db = TestDb.Create();
        var actor = CreateAdmin("actor@example.test", RowVersion);
        db.Admins.Add(actor);
        await db.SaveChangesAsync();
        var sender = new CapturingInvitationEmailSender();
        var hasher = new PasswordHasher<Admin>();
        var service = CreateService(db, sender, passwordHasher: hasher);
        var invitedResult = await service.InviteAsync(actor.AdminId, new InviteAdminDto { Email = "invited@example.test" }, CancellationToken.None);
        var invited = await db.Admins.SingleAsync(item => item.PublicId == invitedResult.Value!.PublicId);
        invited.RowVersion = RowVersion;
        await db.SaveChangesAsync();
        var oldRawToken = QueryHelpers.ParseQuery(sender.InvitationLink!.Query)["token"].ToString();

        var resend = await service.ResendInvitationAsync(
            invited.PublicId,
            actor.AdminId,
            new AdminAccountConcurrencyDto { RowVersion = Convert.ToBase64String(RowVersion) },
            CancellationToken.None);
        var newRawToken = QueryHelpers.ParseQuery(sender.InvitationLink!.Query)["token"].ToString();
        var oldAcceptance = await service.AcceptInvitationAsync(CreateAcceptance(oldRawToken), CancellationToken.None);
        var acceptance = await service.AcceptInvitationAsync(CreateAcceptance(newRawToken), CancellationToken.None);
        var reused = await service.AcceptInvitationAsync(CreateAcceptance(newRawToken), CancellationToken.None);

        Assert.True(resend.IsSuccess);
        Assert.False(oldAcceptance.IsSuccess);
        Assert.True(acceptance.IsSuccess);
        Assert.False(reused.IsSuccess);
        Assert.False(invited.IsInvitationPending);
        Assert.False(invited.MustChangePassword);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(invited, invited.PasswordHash, "Strong-Invited-Password-1!"));
        Assert.All(db.PasswordResetTokens.Where(item => item.AdminId == invited.AdminId), item => Assert.True(item.IsUsed));
    }

    [Fact]
    public async Task Password_reset_token_cannot_be_accepted_as_invitation()
    {
        await using var db = TestDb.Create();
        var admin = CreateAdmin("pending@example.test", RowVersion, pending: true);
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        const string rawToken = "normal-reset-token";
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            AdminId = admin.AdminId,
            Purpose = PasswordResetTokenPurpose.PasswordReset,
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = DateTime.UtcNow,
            ExpirationDate = DateTime.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.AcceptInvitationAsync(CreateAcceptance(rawToken), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(admin.IsInvitationPending);
    }

    [Fact]
    public async Task Deactivation_blocks_self_and_last_completed_admin_but_allows_another_admin()
    {
        await using var db = TestDb.Create();
        var actor = CreateAdmin("actor@example.test", RowVersion);
        var target = CreateAdmin("target@example.test", [8, 7, 6, 5, 4, 3, 2, 1]);
        db.Admins.AddRange(actor, target);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var self = await service.DeactivateAsync(actor.PublicId, actor.AdminId, Concurrency(actor), CancellationToken.None);
        var targetResult = await service.DeactivateAsync(target.PublicId, actor.AdminId, Concurrency(target), CancellationToken.None);
        var last = await service.DeactivateAsync(actor.PublicId, target.AdminId, Concurrency(actor), CancellationToken.None);

        Assert.False(self.IsSuccess);
        Assert.True(targetResult.IsSuccess);
        Assert.False(last.IsSuccess);
        Assert.True(actor.IsActive);
        Assert.False(target.IsActive);
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "SelfDeactivationRejected");
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "LastActiveAdminProtectionTriggered");
        Assert.Contains(db.SecurityAuditLogs, item => item.EventType == "AdminDeactivated");
    }

    [Fact]
    public async Task Stale_row_version_returns_safe_conflict()
    {
        await using var db = TestDb.Create();
        var actor = CreateAdmin("actor@example.test", RowVersion);
        var target = CreateAdmin("target@example.test", [8, 7, 6, 5, 4, 3, 2, 1]);
        db.Admins.AddRange(actor, target);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.DeactivateAsync(
            target.PublicId,
            actor.AdminId,
            new AdminAccountConcurrencyDto { RowVersion = Convert.ToBase64String(RowVersion) },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal("Kayıt başka bir kullanıcı tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.", result.Error);
        await db.Entry(target).ReloadAsync();
        Assert.True(target.IsActive);
    }

    [Fact]
    public async Task Deactivate_revokes_existing_token_and_reactivation_does_not_restore_it()
    {
        await using var db = TestDb.Create();
        var actor = CreateAdmin("actor@example.test", RowVersion);
        var target = CreateAdmin("target@example.test", [8, 7, 6, 5, 4, 3, 2, 1]);
        db.Admins.AddRange(actor, target);
        await db.SaveChangesAsync();
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 7, 21, 9, 0, 0, TimeSpan.Zero));
        var tokenService = new GraduateApp.API.Security.AccessTokenService(new EphemeralDataProtectionProvider(), db, clock);
        var token = tokenService.Issue(target.AdminId.ToString(), GraduateApp.API.Security.ApiAuthenticationDefaults.AdminRole, "Yönetici", target.SecurityStamp).Token;
        var service = CreateService(db);

        Assert.NotNull(await tokenService.ValidateAsync(token, CancellationToken.None));
        var deactivated = await service.DeactivateAsync(target.PublicId, actor.AdminId, Concurrency(target), CancellationToken.None);
        var afterDeactivation = await tokenService.ValidateAsync(token, CancellationToken.None);
        var activated = await service.ActivateAsync(target.PublicId, actor.AdminId, Concurrency(target), CancellationToken.None);
        var afterReactivation = await tokenService.ValidateAsync(token, CancellationToken.None);

        Assert.True(deactivated.IsSuccess);
        Assert.Null(afterDeactivation);
        Assert.True(activated.IsSuccess);
        Assert.Null(afterReactivation);
    }

    [Fact]
    public async Task Pending_and_inactive_admin_tokens_are_rejected_even_with_matching_stamp()
    {
        await using var db = TestDb.Create();
        var pending = CreateAdmin("pending@example.test", RowVersion, pending: true);
        var inactive = CreateAdmin("inactive@example.test", [8, 7, 6, 5, 4, 3, 2, 1]);
        inactive.IsActive = false;
        db.Admins.AddRange(pending, inactive);
        await db.SaveChangesAsync();
        var tokenService = new GraduateApp.API.Security.AccessTokenService(
            new EphemeralDataProtectionProvider(),
            db,
            new TestTimeProvider(DateTimeOffset.UtcNow));
        var pendingToken = tokenService.Issue(pending.AdminId.ToString(), GraduateApp.API.Security.ApiAuthenticationDefaults.AdminRole, "Yönetici", pending.SecurityStamp).Token;
        var inactiveToken = tokenService.Issue(inactive.AdminId.ToString(), GraduateApp.API.Security.ApiAuthenticationDefaults.AdminRole, "Yönetici", inactive.SecurityStamp).Token;

        Assert.Null(await tokenService.ValidateAsync(pendingToken, CancellationToken.None));
        Assert.Null(await tokenService.ValidateAsync(inactiveToken, CancellationToken.None));
    }

    [Fact]
    public async Task Unlock_clears_lockout_and_failures_rotates_stamp_and_audits()
    {
        await using var db = TestDb.Create();
        var actor = CreateAdmin("actor@example.test", RowVersion);
        var target = CreateAdmin("locked@example.test", [8, 7, 6, 5, 4, 3, 2, 1]);
        target.AccessFailedCount = 4;
        target.LockoutEndUtc = new DateTimeOffset(2026, 7, 21, 9, 30, 0, TimeSpan.Zero);
        var oldStamp = target.SecurityStamp;
        db.Admins.AddRange(actor, target);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.UnlockAsync(target.PublicId, actor.AdminId, Concurrency(target), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, target.AccessFailedCount);
        Assert.Null(target.LockoutEndUtc);
        Assert.NotEqual(oldStamp, target.SecurityStamp);
        Assert.Equal("AdminUnlocked", Assert.Single(db.SecurityAuditLogs).EventType);
    }

    [Fact]
    public async Task List_search_status_paging_and_current_marker_are_stable()
    {
        await using var db = TestDb.CreateWithStrictQueryWarnings();
        for (var index = 11; index >= 0; index--)
        {
            var admin = CreateAdmin($"admin{index:D2}@example.test", RowVersion);
            admin.IsActive = index % 3 != 0;
            admin.IsInvitationPending = index == 5;
            db.Admins.Add(admin);
        }

        await db.SaveChangesAsync();
        var current = await db.Admins.SingleAsync(item => item.NormalizedEmail == "ADMIN01@EXAMPLE.TEST");
        var service = CreateService(db);

        var page = await service.GetAsync(null, null, 1, 10, current.AdminId, CancellationToken.None);
        var inactive = await service.GetAsync(null, AdminAccountStatus.Inactive, 1, 100, current.AdminId, CancellationToken.None);
        var search = await service.GetAsync("admin05@", null, 1, 10, current.AdminId, CancellationToken.None);

        Assert.Equal(12, page.TotalCount);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal(page.Items.OrderBy(item => item.Email, StringComparer.Ordinal).Select(item => item.Email), page.Items.Select(item => item.Email));
        Assert.True(page.Items.Single(item => item.PublicId == current.PublicId).IsCurrentAdmin);
        Assert.Equal(4, inactive.TotalCount);
        Assert.All(inactive.Items, item => Assert.False(item.IsActive));
        Assert.Single(search.Items);
        Assert.True(search.Items[0].IsInvitationPending);
    }

    [Fact]
    public async Task Expired_invitation_is_rejected_without_changing_password_or_pending_state()
    {
        await using var db = TestDb.Create();
        var admin = CreateAdmin("pending@example.test", RowVersion, pending: true);
        var hasher = new PasswordHasher<Admin>();
        var originalHash = admin.PasswordHash;
        db.Admins.Add(admin);
        await db.SaveChangesAsync();
        const string rawToken = "expired-invitation-token";
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            AdminId = admin.AdminId,
            Purpose = PasswordResetTokenPurpose.AdminInvitation,
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc),
            ExpirationDate = new DateTime(2026, 7, 21, 8, 59, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, passwordHasher: hasher);

        var result = await service.AcceptInvitationAsync(CreateAcceptance(rawToken), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(admin.IsInvitationPending);
        Assert.Equal(originalHash, admin.PasswordHash);
    }

    [Fact]
    public void Public_dto_excludes_secrets_and_internal_admin_id()
    {
        var names = typeof(AdminAccountDto).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("AdminId", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("SecurityStamp", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", names, StringComparer.OrdinalIgnoreCase);
    }

    private static AdminAccountService CreateService(
        GraduateAppDbContext db,
        CapturingInvitationEmailSender? sender = null,
        CapturingLogger<AdminAccountService>? logger = null,
        IPasswordHasher<Admin>? passwordHasher = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Web:BaseUrl"] = "https://localhost:7272" })
            .Build();
        return new AdminAccountService(
            db,
            passwordHasher ?? new PasswordHasher<Admin>(),
            new InvariantEmailNormalizer(),
            sender ?? new CapturingInvitationEmailSender(),
            Options.Create(new AdminInvitationOptions()),
            configuration,
            new TestTimeProvider(new DateTimeOffset(2026, 7, 21, 9, 0, 0, TimeSpan.Zero)),
            logger ?? new CapturingLogger<AdminAccountService>());
    }

    private static Admin CreateAdmin(string email, byte[] rowVersion, bool pending = false)
    {
        var normalized = email.ToUpperInvariant();
        var admin = new Admin
        {
            PublicId = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = normalized,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true,
            IsInvitationPending = pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = rowVersion
        };
        admin.PasswordHash = new PasswordHasher<Admin>().HashPassword(admin, "Strong-Admin-Password-1!");
        admin.LoginIdentity = new LoginIdentity
        {
            NormalizedEmail = normalized,
            AccountType = LoginAccountType.Admin,
            Admin = admin,
            CreatedAtUtc = DateTime.UtcNow
        };
        return admin;
    }

    private static Student CreateStudent(string email)
    {
        var normalized = email.ToUpperInvariant();
        var student = new Student
        {
            Tc = "10000000146",
            PublicId = Guid.NewGuid(),
            StudentName = "Test",
            StudentSurname = "Öğrenci",
            Email = email,
            NormalizedEmail = normalized,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true
        };
        student.LoginIdentity = new LoginIdentity
        {
            NormalizedEmail = normalized,
            AccountType = LoginAccountType.Student,
            Student = student
        };
        return student;
    }

    private static AdminAccountConcurrencyDto Concurrency(Admin admin) =>
        new() { RowVersion = Convert.ToBase64String(admin.RowVersion) };

    private static AcceptAdminInvitationDto CreateAcceptance(string rawToken) => new()
    {
        Token = rawToken,
        NewPassword = "Strong-Invited-Password-1!",
        ConfirmPassword = "Strong-Invited-Password-1!"
    };

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed class CapturingInvitationEmailSender : IAdminInvitationEmailSender
    {
        public Uri? InvitationLink { get; private set; }

        public Task SendAsync(string recipient, Uri invitationLink, CancellationToken cancellationToken)
        {
            InvitationLink = invitationLink;
            return Task.CompletedTask;
        }
    }
}
