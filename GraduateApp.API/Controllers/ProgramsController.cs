using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Controllers;

[ApiController]
[Route("api/programs")]
public sealed class ProgramsController(GraduateAppDbContext dbContext, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("open")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<OpenProgramDto>>> GetOpen(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return Ok(await dbContext.Programs.AsNoTracking()
            .Where(item => item.IsOpen
                && (!item.ApplicationDeadlineUtc.HasValue || item.ApplicationDeadlineUtc.Value >= now))
            .OrderBy(item => item.ProgramName)
            .Select(item => new OpenProgramDto(
                item.ProgramId,
                item.ProgramName,
                item.DegreeType,
                item.Institute.InstituteName,
                item.ApplicationDeadlineUtc))
            .ToListAsync(cancellationToken));
    }
}
