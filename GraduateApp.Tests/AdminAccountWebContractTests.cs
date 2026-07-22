using System.Reflection;
using GraduateApp.API.Controllers;
using GraduateApp.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Tests;

public sealed class AdminAccountWebContractTests
{
    [Fact]
    public void Account_mutations_are_explicit_posts_with_antiforgery_and_no_client_target_boolean()
    {
        var methods = new[]
        {
            nameof(AdminController.InviteAdmin),
            nameof(AdminController.ResendAdminInvitation),
            nameof(AdminController.ActivateAdmin),
            nameof(AdminController.DeactivateAdmin),
            nameof(AdminController.UnlockAdmin)
        };

        foreach (var methodName in methods)
        {
            var method = typeof(AdminController).GetMethod(methodName);
            Assert.NotNull(method);
            Assert.NotNull(method!.GetCustomAttribute<HttpPostAttribute>());
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
            Assert.DoesNotContain(method.GetParameters(), parameter =>
                parameter.ParameterType == typeof(bool)
                || string.Equals(parameter.Name, "isActive", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Account_view_uses_public_id_row_version_and_hides_self_deactivation()
    {
        var view = Read("GraduateApp.Web", "Views", "Admin", "Accounts.cshtml");

        Assert.Contains("asp-action=\"ActivateAdmin\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"DeactivateAdmin\"", view, StringComparison.Ordinal);
        Assert.Contains("@if (!account.IsCurrentAdmin && account.IsActive)", view, StringComparison.Ordinal);
        Assert.Contains("name=\"publicId\"", view, StringComparison.Ordinal);
        Assert.Contains("name=\"rowVersion\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"adminId\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("name=\"isActive\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invite.Password", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-disable-on-submit=\"true\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_navigation_and_anonymous_invitation_acceptance_are_present()
    {
        var layout = Read("GraduateApp.Web", "Views", "Shared", "_Layout.cshtml");
        var view = Read("GraduateApp.Web", "Views", "Account", "AcceptAdminInvitation.cshtml");
        var get = typeof(AccountController).GetMethod(
            nameof(AccountController.AcceptAdminInvitation),
            [typeof(string)]);
        var post = typeof(AccountController).GetMethod(
            nameof(AccountController.AcceptAdminInvitation),
            [typeof(GraduateApp.Web.Models.AcceptAdminInvitationViewModel), typeof(CancellationToken)]);

        Assert.Contains("asp-action=\"Accounts\"", layout, StringComparison.Ordinal);
        Assert.NotNull(get?.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(post?.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(post?.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(post?.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Contains("asp-for=\"Token\" type=\"hidden\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"@Model.Token\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"@Model.NewPassword\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_invitation_acceptance_is_anonymous_but_account_management_is_admin_only()
    {
        var accept = typeof(AuthController).GetMethod(nameof(AuthController.AcceptAdminInvitation));
        var accountAuthorization = typeof(AdminAccountsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(accept?.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("Admin", accountAuthorization?.Roles);
    }

    private static string Read(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine([root, .. segments]));
    }
}
