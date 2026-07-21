using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed class AdminAccountRaceIntegrationTests
{
    private static readonly Guid FirstPublicId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondPublicId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [LocalDbFact]
    public async Task Concurrent_cross_deactivation_never_leaves_zero_completed_active_admins()
    {
        await using var database = new LocalDbTestDatabase($"GraduateAppAdminRace_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        await database.ExecuteAsync(CurrentSchemaSql());
        var firstRowVersion = await ReadRowVersionAsync(database.ConnectionString, 1);
        var secondRowVersion = await ReadRowVersionAsync(database.ConnectionString, 2);
        var firstRequest = new GraduateApp.API.DTOs.AdminAccountConcurrencyDto { RowVersion = Convert.ToBase64String(firstRowVersion) };
        var secondRequest = new GraduateApp.API.DTOs.AdminAccountConcurrencyDto { RowVersion = Convert.ToBase64String(secondRowVersion) };

        await using var firstContext = CreateContext(database.ConnectionString);
        await using var secondContext = CreateContext(database.ConnectionString);
        var firstService = CreateService(firstContext);
        var secondService = CreateService(secondContext);

        var results = await Task.WhenAll(
            firstService.DeactivateAsync(SecondPublicId, actorAdminId: 1, secondRequest, CancellationToken.None),
            secondService.DeactivateAsync(FirstPublicId, actorAdminId: 2, firstRequest, CancellationToken.None));

        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => !result.IsSuccess && result.StatusCode == StatusCodes.Status409Conflict);
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Admins] WHERE [IsActive] = 1 AND [IsInvitationPending] = 0;"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[SecurityAuditLogs] WHERE [EventType] = N'LastActiveAdminProtectionTriggered';"));
    }

    [LocalDbFact]
    public async Task Concurrent_same_email_invitations_create_exactly_one_admin_and_identity()
    {
        await using var database = new LocalDbTestDatabase($"GraduateAppAdminInviteRace_{Guid.NewGuid():N}", null);
        await database.CreateAsync();
        await database.ExecuteAsync(CurrentSchemaSql());
        await using var firstContext = CreateContext(database.ConnectionString);
        await using var secondContext = CreateContext(database.ConnectionString);
        var request = new GraduateApp.API.DTOs.InviteAdminDto { Email = "new-admin@example.test" };

        var results = await Task.WhenAll(
            CreateService(firstContext).InviteAsync(1, request, CancellationToken.None),
            CreateService(secondContext).InviteAsync(1, request, CancellationToken.None));

        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => !result.IsSuccess && result.StatusCode == StatusCodes.Status409Conflict);
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Admins] WHERE [NormalizedEmail] = N'NEW-ADMIN@EXAMPLE.TEST';"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[LoginIdentities] WHERE [NormalizedEmail] = N'NEW-ADMIN@EXAMPLE.TEST';"));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[PasswordResetTokens] WHERE [Purpose] = N'AdminInvitation';"));
    }

    private static GraduateAppDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<GraduateAppDbContext>().UseSqlServer(connectionString).Options);

    private static AdminAccountService CreateService(GraduateAppDbContext db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Web:BaseUrl"] = "https://localhost:7272" })
            .Build();
        return new AdminAccountService(
            db,
            new PasswordHasher<Admin>(),
            new InvariantEmailNormalizer(),
            new NoOpInvitationSender(),
            Options.Create(new AdminInvitationOptions()),
            configuration,
            new TestTimeProvider(new DateTimeOffset(2026, 7, 21, 9, 0, 0, TimeSpan.Zero)),
            NullLogger<AdminAccountService>.Instance);
    }

    private static async Task<byte[]> ReadRowVersionAsync(string connectionString, int adminId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [RowVersion] FROM [dbo].[Admins] WHERE [AdminID] = @AdminID;";
        command.Parameters.AddWithValue("@AdminID", adminId);
        return (byte[])(await command.ExecuteScalarAsync())!;
    }

    private static string CurrentSchemaSql() =>
        $"""
        CREATE TABLE [dbo].[Admins]
        (
            [AdminID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Admins] PRIMARY KEY,
            [PublicID] uniqueidentifier NOT NULL,
            [Email] nvarchar(254) NOT NULL,
            [NormalizedEmail] nvarchar(254) NOT NULL,
            [PasswordHash] varchar(512) NOT NULL,
            [SecurityStamp] varchar(64) NOT NULL,
            [AccessFailedCount] int NOT NULL,
            [LockoutEndUtc] datetimeoffset NULL,
            [MustChangePassword] bit NOT NULL,
            [IsActive] bit NOT NULL CONSTRAINT [DF_Admins_IsActive] DEFAULT (1),
            [IsInvitationPending] bit NOT NULL CONSTRAINT [DF_Admins_IsInvitationPending] DEFAULT (0),
            [CreatedAtUtc] datetime2 NOT NULL,
            [UpdatedAtUtc] datetime2 NOT NULL,
            [RowVersion] rowversion NOT NULL
        );
        CREATE UNIQUE INDEX [IX_Admins_PublicID] ON [dbo].[Admins] ([PublicID]);
        CREATE UNIQUE INDEX [IX_Admins_NormalizedEmail] ON [dbo].[Admins] ([NormalizedEmail]);

        CREATE TABLE [dbo].[LoginIdentities]
        (
            [LoginIdentityID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LoginIdentities] PRIMARY KEY,
            [NormalizedEmail] nvarchar(254) NOT NULL,
            [AccountType] nvarchar(20) NOT NULL,
            [StudentTC] char(11) NULL,
            [AdminID] int NULL,
            [CreatedAtUtc] datetime2 NOT NULL,
            [RowVersion] rowversion NOT NULL
        );
        CREATE UNIQUE INDEX [IX_LoginIdentities_NormalizedEmail] ON [dbo].[LoginIdentities] ([NormalizedEmail]);
        CREATE UNIQUE INDEX [IX_LoginIdentities_AdminID] ON [dbo].[LoginIdentities] ([AdminID]) WHERE [AdminID] IS NOT NULL;

        CREATE TABLE [dbo].[Students]
        (
            [TC] char(11) NOT NULL CONSTRAINT [PK_Students] PRIMARY KEY,
            [NormalizedEmail] nvarchar(254) NOT NULL
        );

        CREATE TABLE [dbo].[PasswordResetTokens]
        (
            [TokenID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PasswordResetTokens] PRIMARY KEY,
            [TC] char(11) NULL,
            [AdminID] int NULL,
            [TokenHash] varchar(256) NOT NULL,
            [Purpose] nvarchar(32) NOT NULL,
            [ExpirationDate] datetime2 NOT NULL,
            [IsUsed] bit NOT NULL CONSTRAINT [DF_PasswordResetTokens_IsUsed] DEFAULT (0),
            [CreatedAtUtc] datetime2 NOT NULL,
            [RowVersion] rowversion NOT NULL
        );
        CREATE UNIQUE INDEX [IX_PasswordResetTokens_TokenHash] ON [dbo].[PasswordResetTokens] ([TokenHash]);

        CREATE TABLE [dbo].[SecurityAuditLogs]
        (
            [AuditID] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SecurityAuditLogs] PRIMARY KEY,
            [ActorAdminID] int NULL,
            [EventType] nvarchar(100) NOT NULL,
            [TargetType] nvarchar(50) NOT NULL,
            [TargetId] nvarchar(100) NOT NULL,
            [Details] nvarchar(1000) NULL,
            [CreatedAtUtc] datetime2 NOT NULL
        );

        INSERT INTO [dbo].[Admins]
            ([PublicID], [Email], [NormalizedEmail], [PasswordHash], [SecurityStamp], [AccessFailedCount], [LockoutEndUtc], [MustChangePassword], [IsActive], [IsInvitationPending], [CreatedAtUtc], [UpdatedAtUtc])
        VALUES
            ('{FirstPublicId:D}', N'first@example.test', N'FIRST@EXAMPLE.TEST', 'hash-one', 'stamp-one', 0, NULL, 0, 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME()),
            ('{SecondPublicId:D}', N'second@example.test', N'SECOND@EXAMPLE.TEST', 'hash-two', 'stamp-two', 0, NULL, 0, 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME());
        INSERT INTO [dbo].[LoginIdentities] ([NormalizedEmail], [AccountType], [StudentTC], [AdminID], [CreatedAtUtc]) VALUES
            (N'FIRST@EXAMPLE.TEST', N'Admin', NULL, 1, SYSUTCDATETIME()),
            (N'SECOND@EXAMPLE.TEST', N'Admin', NULL, 2, SYSUTCDATETIME());
        """;

    private sealed class NoOpInvitationSender : IAdminInvitationEmailSender
    {
        public Task SendAsync(string recipient, Uri invitationLink, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
