using System.Net;
using System.Security.Claims;
using System.Text;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GraduateApp.Tests;

public sealed class LoginIsolationWebTests
{
    [Fact]
    public void Student_cookie_opening_admin_login_shows_role_and_switch_instead_of_redirecting()
    {
        var controller = CreateController(new LoginResponseHandler("Admin"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(
                CreatePrincipal("Student"),
                CreateRequestServices()),
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor()
        };

        var result = Assert.IsType<ViewResult>(controller.AdminLogin());
        var model = Assert.IsType<LoginViewModel>(result.Model);

        Assert.Equal("Login", result.ViewName);
        Assert.True(model.HasExistingSession);
        Assert.Equal("Öğrenci", model.ExistingRoleDisplay);
        Assert.Equal(LoginAccountType.Admin, model.AccountType);
    }

    [Fact]
    public async Task Admin_login_sends_explicit_account_type_and_creates_only_admin_cookie_claim()
    {
        var handler = new LoginResponseHandler("Admin");
        var authentication = new RecordingAuthenticationService();
        var controller = CreateController(handler);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(requestServices: CreateRequestServices(authentication)),
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor()
        };

        var result = await controller.AdminLogin(new LoginViewModel
        {
            Username = "admin@example.test",
            Password = "Strong-Admin-1!"
        }, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("\"accountType\":\"Admin\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.NotNull(authentication.SignedInPrincipal);
        Assert.True(authentication.SignedInPrincipal!.IsInRole("Admin"));
        Assert.False(authentication.SignedInPrincipal.IsInRole("Student"));
    }

    [Fact]
    public async Task Admin_login_ignores_manipulated_account_type_and_clears_its_binding_error()
    {
        var handler = new LoginResponseHandler("Admin");
        var authentication = new RecordingAuthenticationService();
        var controller = CreateController(handler);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(requestServices: CreateRequestServices(authentication)),
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor()
        };
        controller.ModelState.SetModelValue(
            nameof(LoginViewModel.AccountType),
            new ValueProviderResult("not-a-valid-role"));
        controller.ModelState.AddModelError(
            nameof(LoginViewModel.AccountType),
            "Hesap türü geçersizdir.");

        var result = await controller.AdminLogin(new LoginViewModel
        {
            Username = "admin@example.test",
            Password = "Strong-Admin-1!",
            AccountType = LoginAccountType.Student
        }, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.DoesNotContain(nameof(LoginViewModel.AccountType), controller.ModelState.Keys);
        Assert.Contains("\"accountType\":\"Admin\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.True(authentication.SignedInPrincipal!.IsInRole("Admin"));
    }

    [Fact]
    public async Task Student_login_rejects_an_unexpected_admin_response_without_replacing_cookie()
    {
        var authentication = new RecordingAuthenticationService();
        var controller = CreateController(new LoginResponseHandler("Admin"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(requestServices: CreateRequestServices(authentication)),
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor()
        };

        var result = await controller.Login(new LoginViewModel
        {
            Username = "student@example.test",
            Password = "Strong-Student-1!"
        }, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Null(authentication.SignedInPrincipal);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public void Account_switch_is_post_only_and_antiforgery_protected()
    {
        var method = typeof(AccountController).GetMethod(
            nameof(AccountController.SwitchAccount),
            [typeof(LoginAccountType)]);

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true).SingleOrDefault());
        Assert.NotNull(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).SingleOrDefault());
    }

    [Fact]
    public async Task Account_switch_closes_existing_cookie_before_admin_login()
    {
        var authentication = new RecordingAuthenticationService();
        var controller = CreateController(new LoginResponseHandler("Admin"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(
                CreatePrincipal("Student"),
                CreateRequestServices(authentication)),
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor()
        };

        var result = Assert.IsType<RedirectToActionResult>(
            await controller.SwitchAccount(LoginAccountType.Admin));

        Assert.Equal(1, authentication.SignOutCount);
        Assert.Equal(nameof(AccountController.AdminLogin), result.ActionName);
    }

    [Theory]
    [InlineData("Admin", nameof(AccountController.AdminLogin))]
    [InlineData("Student", nameof(AccountController.Login))]
    public async Task Change_password_redirects_to_login_for_claimed_role(
        string role,
        string expectedAction)
    {
        var authentication = new RecordingAuthenticationService();
        var controller = CreateController(new StaticResponseHandler(HttpStatusCode.NoContent));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(
                CreatePrincipal(role),
                CreateRequestServices(authentication)),
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor()
        };

        var result = Assert.IsType<RedirectToActionResult>(
            await controller.ChangePassword(new ChangePasswordViewModel
            {
                CurrentPassword = "Old-Password-1!",
                NewPassword = "New-Password-1!",
                ConfirmPassword = "New-Password-1!"
            }, CancellationToken.None));

        Assert.Equal(expectedAction, result.ActionName);
        Assert.Equal(1, authentication.SignOutCount);
    }

    [Theory]
    [InlineData("Admin", nameof(AccountController.AdminLogin))]
    [InlineData("Student", nameof(AccountController.Login))]
    public async Task Reset_password_redirects_using_server_side_account_type(
        string accountType,
        string expectedAction)
    {
        var controller = CreateController(new StaticResponseHandler(
            HttpStatusCode.OK,
            $$"""{"accountType":"{{accountType}}"}"""));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(requestServices: CreateRequestServices()),
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor()
        };

        var result = Assert.IsType<RedirectToActionResult>(
            await controller.ResetPassword(new ResetPasswordViewModel
            {
                Token = "server-issued-token",
                NewPassword = "New-Password-1!",
                ConfirmPassword = "New-Password-1!"
            }, CancellationToken.None));

        Assert.Equal(expectedAction, result.ActionName);
    }

    [Fact]
    public void Login_view_exposes_turkish_role_switching_text()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var view = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "GraduateApp.Web",
            "Views",
            "Account",
            "Login.cshtml"));

        Assert.Contains("Yönetici girişi", view, StringComparison.Ordinal);
        Assert.Contains("Öğrenci girişi", view, StringComparison.Ordinal);
        Assert.Contains("Farklı hesapla giriş yap", view, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"AccountType\"", view, StringComparison.Ordinal);
    }

    private static AccountController CreateController(HttpMessageHandler handler) =>
        new(new GraduateApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test")
        }));

    private static DefaultHttpContext CreateHttpContext(
        ClaimsPrincipal? principal = null,
        IServiceProvider? requestServices = null) => new()
        {
            User = principal ?? new ClaimsPrincipal(new ClaimsIdentity()),
            RequestServices = requestServices ?? new ServiceCollection().BuildServiceProvider()
        };

    private static IServiceProvider CreateRequestServices(
        IAuthenticationService? authentication = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        if (authentication is not null)
        {
            services.AddSingleton<IAuthenticationService>(authentication);
        }

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreatePrincipal(string role) => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "Test Kullanıcı"),
            new Claim(ClaimTypes.Role, role)
        ],
        CookieAuthenticationDefaults.AuthenticationScheme));

    private sealed class LoginResponseHandler(string role) : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var json = $$"""
                {
                  "accessToken": "token-{{role}}",
                  "expiresAtUtc": "2030-01-01T00:00:00+00:00",
                  "role": "{{role}}",
                  "displayName": "Test Kullanıcı",
                  "mustChangePassword": false
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StaticResponseHandler(
        HttpStatusCode statusCode,
        string? json = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);
            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public ClaimsPrincipal? SignedInPrincipal { get; private set; }
        public int SignOutCount { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            SignedInPrincipal = principal;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignOutCount++;
            return Task.CompletedTask;
        }
    }
}
