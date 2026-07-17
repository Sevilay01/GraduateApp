using System.Security.Claims;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Web.Controllers;

public sealed class AccountController(GraduateApiClient apiClient) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectForRole();
        }

        return View(new LoginViewModel { ReturnUrl = NormalizeReturnUrl(returnUrl) });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        model.ReturnUrl = NormalizeReturnUrl(model.ReturnUrl);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await apiClient.LoginAsync(model, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Giriş yapılamadı.");
            return View(model);
        }

        if (result.Value.Role is not ("Student" or "Admin") || string.IsNullOrWhiteSpace(result.Value.AccessToken))
        {
            ModelState.AddModelError(string.Empty, "Kimlik doğrulama yanıtı geçersiz.");
            return View(model);
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, result.Value.DisplayName),
            new Claim(ClaimTypes.Role, result.Value.Role),
            new Claim(ApiSessionConstants.AccessTokenClaim, result.Value.AccessToken)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = result.Value.ExpiresAtUtc,
                AllowRefresh = false
            });

        if (result.Value.MustChangePassword)
        {
            TempData["WarningMessage"] = "İlk girişinizde parolanızı değiştirmeniz gerekiyor.";
            return RedirectToAction(nameof(ChangePassword));
        }

        if (model.ReturnUrl is not null)
        {
            return LocalRedirect(model.ReturnUrl);
        }

        return result.Value.Role == "Admin"
            ? RedirectToAction("Index", "Admin")
            : RedirectToAction("Index", "Panel");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await apiClient.RegisterAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Kayıt oluşturulamadı.");
            return View(model);
        }

        TempData["SuccessMessage"] = "Kaydınız oluşturuldu. Şimdi giriş yapabilirsiniz.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await apiClient.ForgotPasswordAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "İstek şu anda tamamlanamadı.");
            return View(model);
        }

        ViewData["RequestSubmitted"] = true;
        ModelState.Clear();
        return View(new ForgotPasswordViewModel());
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPassword(string? token) =>
        View(new ResetPasswordViewModel { Token = token ?? string.Empty });

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await apiClient.ResetPasswordAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Parola sıfırlanamadı.");
            return View(model);
        }

        TempData["SuccessMessage"] = "Parolanız güncellendi. Yeni parolanızla giriş yapabilirsiniz.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await apiClient.ChangePasswordAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Parola değiştirilemedi.");
            return View(model);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["SuccessMessage"] = "Parolanız değiştirildi. Lütfen tekrar giriş yapın.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        _ = await apiClient.LogoutAsync(cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult AccessDenied() => View();

    private IActionResult RedirectForRole() => User.IsInRole("Admin")
        ? RedirectToAction("Index", "Admin")
        : RedirectToAction("Index", "Panel");

    private string? NormalizeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;
}
