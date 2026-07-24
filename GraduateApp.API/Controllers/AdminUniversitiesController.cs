using System.Security.Claims;
using GraduateApp.API.DTOs;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.API.Controllers;

[ApiController]
[Route("api/admin/universities")]
[Authorize(
    AuthenticationSchemes = ApiAuthenticationDefaults.Scheme,
    Roles = ApiAuthenticationDefaults.AdminRole)]
public sealed class AdminUniversitiesController(
    IUniversityCatalogService universityCatalogService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(await universityCatalogService.GetAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        UniversityCreateDto request,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
        {
            return Unauthorized();
        }

        var result = await universityCatalogService.CreateAsync(adminId, request, cancellationToken);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }
}
