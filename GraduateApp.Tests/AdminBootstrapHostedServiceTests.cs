using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GraduateApp.Tests;

public sealed class AdminBootstrapHostedServiceTests
{
    [Fact]
    public async Task Bootstrap_rejects_invalid_email_without_creating_admin()
    {
        await using var provider = CreateProvider();
        var service = CreateService(provider, "not-an-email", "Bootstrap-Password-1!");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Equal("Bootstrap admin e-posta adresi geçersiz.", exception.Message);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GraduateAppDbContext>();
        Assert.Empty(db.Admins);
        Assert.Empty(db.LoginIdentities);
    }

    [Fact]
    public async Task Bootstrap_rejects_student_owned_email_without_creating_admin()
    {
        await using var provider = CreateProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GraduateAppDbContext>();
            var student = CreateStudent("student@example.test");
            db.Students.Add(student);
            await db.SaveChangesAsync();
        }

        var service = CreateService(provider, "  STUDENT@Example.Test ", "Bootstrap-Password-1!");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Contains("çakışıyor", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("student@example.test", exception.Message, StringComparison.OrdinalIgnoreCase);
        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<GraduateAppDbContext>();
        Assert.Empty(verificationDb.Admins);
        Assert.Single(verificationDb.LoginIdentities);
    }

    [Fact]
    public async Task Bootstrap_restart_and_secret_removal_preserve_existing_admin_and_password()
    {
        await using var provider = CreateProvider();
        var firstStart = CreateService(provider, "admin@example.test", "Bootstrap-Password-1!");
        await firstStart.StartAsync(CancellationToken.None);

        string originalHash;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GraduateAppDbContext>();
            originalHash = (await db.Admins.SingleAsync()).PasswordHash;
        }

        var restart = CreateService(provider, " ADMIN@EXAMPLE.TEST ", "Different-Password-1!");
        await restart.StartAsync(CancellationToken.None);
        var withoutSecret = CreateService(provider, null, null);
        await withoutSecret.StartAsync(CancellationToken.None);

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<GraduateAppDbContext>();
        var admin = await verificationDb.Admins.SingleAsync();
        Assert.Equal(originalHash, admin.PasswordHash);
        Assert.Single(verificationDb.LoginIdentities);
        Assert.Equal(LoginAccountType.Admin, verificationDb.LoginIdentities.Single().AccountType);
    }

    [Fact]
    public async Task Existing_admin_blocks_different_bootstrap_email_without_creating_second_admin()
    {
        await using var provider = CreateProvider();
        await CreateService(provider, "first.admin@example.test", "Bootstrap-Password-1!")
            .StartAsync(CancellationToken.None);

        string originalHash;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GraduateAppDbContext>();
            originalHash = (await db.Admins.SingleAsync()).PasswordHash;
        }

        var secondBootstrap = CreateService(
            provider,
            "second.admin@example.test",
            "Different-Password-1!");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => secondBootstrap.StartAsync(CancellationToken.None));

        Assert.Equal(
            "Sistemde zaten bir yönetici hesabı var; yeni yönetici bootstrap üzerinden oluşturulamaz.",
            exception.Message);
        await using var verificationScope = provider.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<GraduateAppDbContext>();
        Assert.Single(verificationDb.Admins);
        Assert.Single(verificationDb.LoginIdentities);
        Assert.Equal(originalHash, verificationDb.Admins.Single().PasswordHash);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<GraduateAppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IPasswordHasher<Admin>, PasswordHasher<Admin>>();
        return services.BuildServiceProvider();
    }

    private static AdminBootstrapHostedService CreateService(
        ServiceProvider provider,
        string? email,
        string? password)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Email"] = email,
                ["BootstrapAdmin:Password"] = password
            })
            .Build();
        return new AdminBootstrapHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<AdminBootstrapHostedService>.Instance,
            TimeProvider.System,
            new InvariantEmailNormalizer());
    }

    private static Student CreateStudent(string email)
    {
        var student = new Student
        {
            Tc = "10000000146",
            StudentName = "Test",
            StudentSurname = "Öğrenci",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString("N")
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
