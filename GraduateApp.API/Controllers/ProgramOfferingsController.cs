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
public sealed class ProgramOfferingsController(
    IProgramOfferingService offeringService,
    IOfferingDocumentRequirementService documentRequirementService,
    IEvaluationPolicyService evaluationPolicyService) : ControllerBase
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

    [HttpPost("{id:int}/close-for-remediation")]
    public async Task<IActionResult> CloseInvalidForRemediation(
        int id,
        ProgramOfferingRemediationCloseDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await offeringService.CloseInvalidForRemediationAsync(
            id,
            adminId,
            request,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpGet("{id:int}/document-requirements")]
    public async Task<IActionResult> GetDocumentRequirements(int id, CancellationToken cancellationToken) =>
        Ok(await documentRequirementService.GetAsync(id, cancellationToken));

    [HttpPost("{id:int}/document-requirements")]
    public async Task<IActionResult> CreateDocumentRequirement(
        int id,
        OfferingDocumentRequirementCreateDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await documentRequirementService.CreateAsync(id, adminId, request, cancellationToken);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPut("{id:int}/document-requirements/{publicId:guid}")]
    public async Task<IActionResult> UpdateDocumentRequirement(
        int id,
        Guid publicId,
        OfferingDocumentRequirementUpdateDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await documentRequirementService.UpdateAsync(id, publicId, adminId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPost("{id:int}/document-requirements/{publicId:guid}/active")]
    public async Task<IActionResult> SetDocumentRequirementActive(
        int id,
        Guid publicId,
        DocumentRequirementActiveDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await documentRequirementService.SetActiveAsync(id, publicId, adminId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpGet("{id:int}/evaluation-criteria")]
    public async Task<IActionResult> GetEvaluationCriteria(int id, CancellationToken cancellationToken) =>
        Ok(await evaluationPolicyService.GetAsync(id, cancellationToken));

    [HttpPost("{id:int}/evaluation-criteria")]
    public async Task<IActionResult> CreateEvaluationCriterion(
        int id,
        EvaluationCriterionCreateDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await evaluationPolicyService.CreateAsync(id, adminId, request, cancellationToken);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPut("{id:int}/evaluation-criteria/{publicId:guid}")]
    public async Task<IActionResult> UpdateEvaluationCriterion(
        int id,
        Guid publicId,
        EvaluationCriterionUpdateDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await evaluationPolicyService.UpdateAsync(id, publicId, adminId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpDelete("{id:int}/evaluation-criteria/{publicId:guid}")]
    public async Task<IActionResult> DeleteEvaluationCriterion(
        int id,
        Guid publicId,
        EvaluationCriterionDeleteDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await evaluationPolicyService.DeleteAsync(id, publicId, adminId, request, cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    private bool TryGetAdminId(out int adminId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out adminId);
}
