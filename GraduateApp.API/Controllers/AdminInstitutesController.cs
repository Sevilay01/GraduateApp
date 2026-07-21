using System.Security.Claims;
using GraduateApp.API.DTOs;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.API.Controllers;

[ApiController]
[Route("api/admin/institutes")]
[Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.AdminRole)]
public sealed class AdminInstitutesController(IInstituteAdminService instituteService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await instituteService.GetAsync(search, isActive, page, pageSize, cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var institute = await instituteService.GetByIdAsync(id, cancellationToken);
        return institute is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: "Enstitü bulunamadı.")
            : Ok(institute);
    }

    [HttpPost]
    public async Task<IActionResult> Create(InstituteCreateDto request, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await instituteService.CreateAsync(adminId, request, cancellationToken);
        return FromResult(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        InstituteUpdateDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await instituteService.UpdateAsync(id, adminId, request, cancellationToken);
        return FromResult(result);
    }

    [HttpPost("{id:int}/activate")]
    public Task<IActionResult> Activate(
        int id,
        CatalogConcurrencyDto request,
        CancellationToken cancellationToken) =>
        SetActiveAsync(id, true, request, cancellationToken);

    [HttpPost("{id:int}/deactivate")]
    public Task<IActionResult> Deactivate(
        int id,
        CatalogConcurrencyDto request,
        CancellationToken cancellationToken) =>
        SetActiveAsync(id, false, request, cancellationToken);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(
        int id,
        [FromQuery] string rowVersion,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Enstitü eşzamanlılık bilgisi zorunludur.");
        }

        var result = await instituteService.DeleteAsync(id, adminId, rowVersion, cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    private async Task<IActionResult> SetActiveAsync(
        int id,
        bool isActive,
        CatalogConcurrencyDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await instituteService.SetActiveAsync(id, adminId, isActive, request, cancellationToken);
        return FromResult(result);
    }

    private IActionResult FromResult<T>(ServiceResult<T> result) =>
        result.IsSuccess
            ? StatusCode(result.StatusCode, result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);

    private bool TryGetAdminId(out int adminId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out adminId);
}
