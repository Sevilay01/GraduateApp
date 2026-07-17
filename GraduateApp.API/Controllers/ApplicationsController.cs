using System.Security.Claims;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.API.Controllers;

[ApiController]
[Route("api/applications")]
public sealed class ApplicationsController(IApplicationService applicationService) : ControllerBase
{
    [HttpPost]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.StudentRole)]
    public async Task<IActionResult> Create(ApplicationCreateDto request, CancellationToken cancellationToken)
    {
        var studentTc = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(studentTc))
        {
            return Unauthorized();
        }

        var result = await applicationService.CreateAsync(studentTc, request.ProgramOfferingId, cancellationToken);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpGet("mine")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.StudentRole)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var studentTc = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(studentTc))
        {
            return Unauthorized();
        }

        return Ok(await applicationService.GetForStudentAsync(studentTc, cancellationToken));
    }

    [HttpGet("admin")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.AdminRole)]
    public async Task<IActionResult> GetForAdmin(
        [FromQuery] string? search,
        [FromQuery] ApplicationStatus? status,
        [FromQuery] int? academicYearStart,
        [FromQuery] AcademicTerm? term,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await applicationService.GetForAdminAsync(
            search,
            status,
            academicYearStart,
            term,
            page,
            pageSize,
            cancellationToken));

    [HttpGet("admin/{id:int}")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.AdminRole)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken cancellationToken)
    {
        var application = await applicationService.GetDetailForAdminAsync(id, cancellationToken);
        return application is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: "Başvuru bulunamadı.")
            : Ok(application);
    }

    [HttpPut("admin/{id:int}/status")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.AdminRole)]
    public async Task<IActionResult> UpdateStatus(
        int id,
        ApplicationStatusUpdateDto request,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
        {
            return Unauthorized();
        }

        var result = await applicationService.UpdateStatusAsync(id, adminId, request, cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }
}
