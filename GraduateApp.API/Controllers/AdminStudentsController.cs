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

    [HttpGet("{tc}")]
    public async Task<IActionResult> GetDetail(string tc, CancellationToken cancellationToken)
    {
        var student = await studentService.GetDetailAsync(tc, cancellationToken);
        return student is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: "Öğrenci bulunamadı.")
            : Ok(student);
    }

    [HttpPost("{tc}/deactivate")]
    public async Task<IActionResult> Deactivate(string tc, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
        {
            return Unauthorized();
        }

        var result = await studentService.DeactivateAsync(tc, adminId, cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }
}
