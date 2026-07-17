using System.Security.Claims;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.API.Controllers;

[ApiController]
[Route("api/program-offerings")]
[Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.AdminRole)]
public sealed class ProgramOfferingsController(IProgramOfferingService offeringService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int? academicYearStart,
        [FromQuery] AcademicTerm? term,
        [FromQuery] bool includeArchived = false,
        CancellationToken cancellationToken = default) =>
        Ok(await offeringService.GetForAdminAsync(academicYearStart, term, includeArchived, cancellationToken));

    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog(CancellationToken cancellationToken) =>
        Ok(await offeringService.GetCatalogAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(ProgramOfferingCreateDto request, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await offeringService.CreateAsync(adminId, request, cancellationToken);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        ProgramOfferingUpdateDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await offeringService.UpdateAsync(id, adminId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    private bool TryGetAdminId(out int adminId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out adminId);
}
