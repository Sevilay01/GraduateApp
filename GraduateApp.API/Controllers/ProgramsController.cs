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
    public async Task<ActionResult<IReadOnlyList<OpenProgramDto>>> GetOpen(
        [FromQuery] OpenProgramSearchQueryDto request,
        CancellationToken cancellationToken)
    {
        var validationProblem = ValidateSearch(request);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        var search = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : request.Search.Trim();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var draft = ApplicationStatus.Draft.ToString();
        var withdrawn = ApplicationStatus.Withdrawn.ToString();
        var query = dbContext.ProgramOfferings.AsNoTracking()
            .Where(item => item.Program.IsActive
                && item.Program.Institute.IsActive
                && item.IsOpen
                && !item.IsArchived
                && item.ApplicationStartUtc.HasValue
                && item.ApplicationStartUtc.Value <= now
                && item.ApplicationDeadlineUtc.HasValue
                && item.ApplicationDeadlineUtc.Value >= now
                && item.Quota > 0
                && (item.Term == AcademicTerm.LegacyUnspecified
                    || item.DocumentRequirements.Any(requirement =>
                        requirement.IsActive && requirement.IsRequired))
                && (item.UsesEvaluationWorkflow
                    || item.Applications.Count(application => application.CurrentStatus != withdrawn && application.CurrentStatus != draft) < item.Quota));

        if (search is not null)
        {
            query = query.Where(item =>
                item.Program.ProgramName.Contains(search)
                || item.Program.Institute.InstituteName.Contains(search)
                || (item.Program.DegreeType != null && item.Program.DegreeType.Contains(search)));
        }

        if (request.AcademicYearStart.HasValue)
        {
            query = query.Where(item => item.AcademicYearStart == request.AcademicYearStart.Value);
        }

        if (request.Term.HasValue)
        {
            query = query.Where(item => item.Term == request.Term.Value);
        }

        var offerings = await query
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

    private ActionResult<IReadOnlyList<OpenProgramDto>>? ValidateSearch(OpenProgramSearchQueryDto request)
    {
        if (request.Search?.Trim().Length > OpenProgramSearchQueryDto.MaximumSearchLength)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Arama metni en fazla 100 karakter olabilir.");
        }

        if (request.AcademicYearStart is < 2000 or > 2200)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Akademik yıl 2000 ile 2200 arasında olmalıdır.");
        }

        if (request.Term.HasValue
            && request.Term is not AcademicTerm.Fall
                and not AcademicTerm.Spring
                and not AcademicTerm.Summer)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Geçerli bir akademik dönem seçiniz.");
        }

        return null;
    }
}
