using System.Security.Claims;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.API.Controllers;

[ApiController]
[Route("api/admin/students")]
[Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.AdminRole)]
public sealed class AdminStudentsController(IAdminStudentService studentService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await studentService.GetAsync(search, page, pageSize, cancellationToken));

    [HttpGet("{publicId:guid}")]
    public async Task<IActionResult> GetDetail(Guid publicId, CancellationToken cancellationToken)
    {
        var student = await studentService.GetDetailAsync(publicId, cancellationToken);
        return student is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: "Öğrenci bulunamadı.")
            : Ok(student);
    }

    [HttpPost("{publicId:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid publicId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
        {
            return Unauthorized();
        }

        var result = await studentService.DeactivateAsync(publicId, adminId, cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPost("{publicId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid publicId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
        {
            return Unauthorized();
        }

        var result = await studentService.ActivateAsync(publicId, adminId, cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }
}
