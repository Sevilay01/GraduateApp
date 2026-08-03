using System.Security.Claims;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.API.Controllers;

[ApiController]
[Route("api/admin/programs")]
[Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.AdminRole)]
public sealed class AdminProgramsController(IProgramAdminService programService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int? instituteId,
        [FromQuery] string? degreeType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        string? canonicalDegreeType = null;
        if (!string.IsNullOrWhiteSpace(degreeType)
            && !DegreeTypeCatalog.TryCanonicalize(degreeType, out canonicalDegreeType))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Derece türü desteklenen değerlerden biri olmalıdır.");
        }

        return Ok(await programService.GetAsync(
            search,
            isActive,
            instituteId,
            canonicalDegreeType,
            page,
            pageSize,
            cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var program = await programService.GetByIdAsync(id, cancellationToken);
        return program is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: "Program bulunamadı.")
            : Ok(program);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ProgramCreateDto request, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await programService.CreateAsync(adminId, request, cancellationToken);
        return FromResult(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        ProgramUpdateDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await programService.UpdateAsync(id, adminId, request, cancellationToken);
        return FromResult(result);
    }

    [HttpPut("translations")]
    public async Task<IActionResult> UpdateTranslations(
        ProgramTranslationBatchUpdateDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await programService.UpdateTranslationsAsync(
            adminId,
            request,
            cancellationToken);
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
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Program eşzamanlılık bilgisi zorunludur.");
        }

        var result = await programService.DeleteAsync(id, adminId, rowVersion, cancellationToken);
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

        var result = await programService.SetActiveAsync(id, adminId, isActive, request, cancellationToken);
        return FromResult(result);
    }

    private IActionResult FromResult<T>(ServiceResult<T> result) =>
        result.IsSuccess
            ? StatusCode(result.StatusCode, result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);

    private bool TryGetAdminId(out int adminId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out adminId);
}
