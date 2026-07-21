using GraduateApp.API.DTOs;
using GraduateApp.API.Domain;
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
        var withdrawn = ApplicationStatus.Withdrawn.ToString();
        var offerings = await dbContext.ProgramOfferings.AsNoTracking()
            .Where(item => item.Program.IsActive
                && item.Program.Institute.IsActive
                && item.IsOpen
                && !item.IsArchived
                && item.ApplicationStartUtc.HasValue
                && item.ApplicationStartUtc.Value <= now
                && item.ApplicationDeadlineUtc.HasValue
                && item.ApplicationDeadlineUtc.Value >= now
                && item.Quota > 0
                && item.Applications.Count(application => application.CurrentStatus != withdrawn) < item.Quota)
            .OrderBy(item => item.Program.Institute.InstituteName)
            .ThenBy(item => item.Program.ProgramName)
            .ThenBy(item => item.Program.DegreeType)
            .ThenBy(item => item.AcademicYearStart)
            .ThenBy(item => item.Term)
            .ThenBy(item => item.ProgramOfferingId)
            .Include(item => item.Program).ThenInclude(item => item.Institute)
            .Include(item => item.ExamRequirements).ThenInclude(item => item.Exam)
            .ToListAsync(cancellationToken);

        return Ok(offerings.Select(item => new OpenProgramDto(
                item.ProgramOfferingId,
                item.ProgramId,
                item.Program.ProgramName,
                item.Program.DegreeType,
                item.Program.Institute.InstituteName,
                item.AcademicYearStart,
                AcademicPeriodFormatter.FormatAcademicYear(item.AcademicYearStart),
                item.Term,
                AcademicPeriodFormatter.FormatTerm(item.Term),
                item.ApplicationStartUtc.GetValueOrDefault(),
                item.ApplicationDeadlineUtc.GetValueOrDefault(),
                item.Quota,
                item.ExamRequirements
                    .OrderBy(requirement => requirement.Exam.ExamName)
                    .Select(requirement => new ExamRequirementDto(
                        requirement.ExamId,
                        requirement.Exam.ExamName,
                        requirement.MinimumScore,
                        requirement.MinimumValidityDate,
                        requirement.IsRequired))
                    .ToArray()))
            .ToArray());
    }
}
