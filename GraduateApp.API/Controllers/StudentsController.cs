using System.Security.Claims;
using GraduateApp.API.DTOs;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.API.Controllers;

[ApiController]
[Route("api/students")]
[Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.StudentRole)]
public sealed class StudentsController(IStudentProfileService profileService) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var studentTc = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(studentTc))
        {
            return Unauthorized();
        }

        var profile = await profileService.GetAsync(studentTc, cancellationToken);
        return profile is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: "Öğrenci profili bulunamadı.")
            : Ok(profile);
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile(UpdateStudentProfileDto request, CancellationToken cancellationToken)
    {
        var studentTc = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(studentTc))
        {
            return Unauthorized();
        }

        var result = await profileService.UpdateAsync(studentTc, request, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpGet("universities")]
    public async Task<IActionResult> GetUniversities(CancellationToken cancellationToken) =>
        Ok(await profileService.GetUniversitiesAsync(cancellationToken));
}
