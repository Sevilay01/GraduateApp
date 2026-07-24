using System.Security.Claims;
using GraduateApp.API.DTOs;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.API.Controllers;

[ApiController]
[Route("api/evaluations")]
[Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.AdminRole)]
public sealed class EvaluationsController(IApplicationEvaluationService evaluationService) : ControllerBase
{
    [HttpGet("offerings/{offeringId:int}")]
    public async Task<IActionResult> Get(int offeringId, CancellationToken cancellationToken)
    {
        var page = await evaluationService.GetAdminPageAsync(offeringId, cancellationToken);
        return page is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: "Değerlendirme ilanı bulunamadı.")
            : Ok(page);
    }

    [HttpGet("offerings/{offeringId:int}/preview")]
    public async Task<IActionResult> Preview(int offeringId, CancellationToken cancellationToken)
    {
        var result = await evaluationService.PreviewAsync(offeringId, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPost("applications/{applicationPublicId:guid}/eligibility")]
    public async Task<IActionResult> DecideEligibility(
        Guid applicationPublicId,
        EligibilityDecisionDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await evaluationService.DecideEligibilityAsync(
            applicationPublicId,
            adminId,
            request,
            cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPost("applications/{applicationPublicId:guid}/criteria/{criterionPublicId:guid}/score")]
    public async Task<IActionResult> SetManualScore(
        Guid applicationPublicId,
        Guid criterionPublicId,
        ManualEvaluationScoreDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await evaluationService.SetManualScoreAsync(
            applicationPublicId,
            criterionPublicId,
            adminId,
            request,
            cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPost("offerings/{offeringId:int}/finalize")]
    public async Task<IActionResult> Finalize(
        int offeringId,
        OfferingEvaluationCommandDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await evaluationService.FinalizeAsync(offeringId, adminId, request, cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpGet("offerings/{offeringId:int}/publication-summary")]
    public async Task<IActionResult> GetPublicationSummary(int offeringId, CancellationToken cancellationToken)
    {
        var result = await evaluationService.GetPublicationSummaryAsync(offeringId, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPost("offerings/{offeringId:int}/publish")]
    public async Task<IActionResult> Publish(
        int offeringId,
        OfferingEvaluationCommandDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId))
        {
            return Unauthorized();
        }

        var result = await evaluationService.PublishAsync(offeringId, adminId, request, cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    private bool TryGetAdminId(out int adminId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out adminId);
}
