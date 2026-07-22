using System.Security.Claims;
using GraduateApp.API.DTOs;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.API.Controllers;

[ApiController]
[Route("api/admin/accounts")]
[Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.AdminRole)]
public sealed class AdminAccountsController(IAdminAccountService accountService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? search,
        [FromQuery] AdminAccountStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return TryGetAdminId(out var adminId)
            ? Ok(await accountService.GetAsync(search, status, page, pageSize, adminId, cancellationToken))
            : Unauthorized();
    }

    [HttpGet("{publicId:guid}")]
    public async Task<IActionResult> GetByPublicId(Guid publicId, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var account = await accountService.GetByPublicIdAsync(publicId, adminId, cancellationToken);
        return account is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: "Yönetici hesabı bulunamadı.")
            : Ok(account);
    }

    [HttpPost("invitations")]
    public async Task<IActionResult> Invite(InviteAdminDto request, CancellationToken cancellationToken) =>
        TryGetAdminId(out var adminId)
            ? FromResult(await accountService.InviteAsync(adminId, request, cancellationToken))
            : Unauthorized();

    [HttpPost("{publicId:guid}/resend-invitation")]
    public async Task<IActionResult> ResendInvitation(Guid publicId, AdminAccountConcurrencyDto request, CancellationToken cancellationToken) =>
        TryGetAdminId(out var adminId)
            ? FromResult(await accountService.ResendInvitationAsync(publicId, adminId, request, cancellationToken))
            : Unauthorized();

    [HttpPost("{publicId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid publicId, AdminAccountConcurrencyDto request, CancellationToken cancellationToken) =>
        TryGetAdminId(out var adminId)
            ? FromResult(await accountService.ActivateAsync(publicId, adminId, request, cancellationToken))
            : Unauthorized();

    [HttpPost("{publicId:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid publicId, AdminAccountConcurrencyDto request, CancellationToken cancellationToken) =>
        TryGetAdminId(out var adminId)
            ? FromResult(await accountService.DeactivateAsync(publicId, adminId, request, cancellationToken))
            : Unauthorized();

    [HttpPost("{publicId:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid publicId, AdminAccountConcurrencyDto request, CancellationToken cancellationToken) =>
        TryGetAdminId(out var adminId)
            ? FromResult(await accountService.UnlockAsync(publicId, adminId, request, cancellationToken))
            : Unauthorized();

    private IActionResult FromResult(ServiceResult<AdminAccountDto> result) =>
        result.IsSuccess
            ? StatusCode(result.StatusCode, result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);

    private bool TryGetAdminId(out int adminId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out adminId);
}
