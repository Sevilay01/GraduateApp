using System.Security.Claims;
using GraduateApp.API.DTOs;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GraduateApp.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService, IAdminAccountService adminAccountService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("register")]
    public async Task<IActionResult> Register(RegisterStudentDto request, CancellationToken cancellationToken)
    {
        var result = await authService.RegisterStudentAsync(request, cancellationToken);
        return result.IsSuccess
            ? StatusCode(result.StatusCode)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginDto request, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("password-reset")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto request, CancellationToken cancellationToken)
    {
        await authService.RequestPasswordResetAsync(request, cancellationToken);
        return Accepted(new
        {
            message = "Hesap mevcutsa parola sıfırlama yönergeleri gönderildi."
        });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("password-reset")]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto request, CancellationToken cancellationToken)
    {
        var result = await authService.ResetPasswordAsync(request, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPost("admin-invitations/accept")]
    [AllowAnonymous]
    [EnableRateLimiting("password-reset")]
    public async Task<IActionResult> AcceptAdminInvitation(
        AcceptAdminInvitationDto request,
        CancellationToken cancellationToken)
    {
        var result = await adminAccountService.AcceptInvitationAsync(request, cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPost("change-password")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme)]
    [EnableRateLimiting("password-reset")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto request, CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(role))
        {
            return Unauthorized();
        }

        var result = await authService.ChangePasswordAsync(subject, role, request, cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (!string.IsNullOrWhiteSpace(subject) && !string.IsNullOrWhiteSpace(role))
        {
            await authService.RevokeSessionsAsync(subject, role, cancellationToken);
        }

        return NoContent();
    }
}
