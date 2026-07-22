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
public sealed class ApplicationsController(
    IApplicationService applicationService,
    IApplicationDocumentService documentService,
    IApplicationEvaluationService evaluationService) : ControllerBase
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

    [HttpGet("mine/{publicId:guid}")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.StudentRole)]
    public async Task<IActionResult> GetMineDetail(Guid publicId, CancellationToken cancellationToken)
    {
        var studentTc = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(studentTc))
        {
            return Unauthorized();
        }

        var application = await applicationService.GetDetailForStudentAsync(studentTc, publicId, cancellationToken);
        return application is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: "Başvuru bulunamadı.")
            : Ok(application);
    }

    [HttpPost("mine/{publicId:guid}/submit")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.StudentRole)]
    public async Task<IActionResult> Submit(Guid publicId, CancellationToken cancellationToken)
    {
        var studentTc = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(studentTc))
        {
            return Unauthorized();
        }

        var result = await applicationService.SubmitAsync(studentTc, publicId, cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpGet("mine/{publicId:guid}/evaluation-result")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.StudentRole)]
    public async Task<IActionResult> GetMineEvaluationResult(Guid publicId, CancellationToken cancellationToken)
    {
        var studentTc = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(studentTc))
        {
            return Unauthorized();
        }

        var result = await evaluationService.GetPublishedForStudentAsync(studentTc, publicId, cancellationToken);
        return result is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: "Yayımlanmış başvuru sonucu bulunamadı.")
            : Ok(result);
    }

    [HttpPost("mine/{publicId:guid}/document-requirements/{requirementPublicId:guid}/upload")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.StudentRole)]
    [RequestFormLimits(MultipartBodyLengthLimit = 104923136)]
    [RequestSizeLimit(104923136)]
    public async Task<IActionResult> Upload(
        Guid publicId,
        Guid requirementPublicId,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        var studentTc = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(studentTc))
        {
            return Unauthorized();
        }

        var result = await documentService.UploadAsync(
            studentTc,
            publicId,
            requirementPublicId,
            file,
            cancellationToken);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpGet("mine/{publicId:guid}/documents/{documentPublicId:guid}/download")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.StudentRole)]
    public async Task<IActionResult> DownloadMine(
        Guid publicId,
        Guid documentPublicId,
        CancellationToken cancellationToken)
    {
        var studentTc = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(studentTc))
        {
            return Unauthorized();
        }

        var result = await documentService.OpenForStudentAsync(studentTc, publicId, documentPublicId, cancellationToken);
        return result.IsSuccess && result.Value is not null
            ? SecureDownload(result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
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

    [HttpGet("admin/{publicId:guid}")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.AdminRole)]
    public async Task<IActionResult> GetDetail(Guid publicId, CancellationToken cancellationToken)
    {
        var application = await applicationService.GetDetailForAdminAsync(publicId, cancellationToken);
        return application is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: "Başvuru bulunamadı.")
            : Ok(application);
    }

    [HttpPost("admin/{publicId:guid}/status")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.AdminRole)]
    public async Task<IActionResult> UpdateStatus(
        Guid publicId,
        ApplicationStatusUpdateDto request,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
        {
            return Unauthorized();
        }

        var result = await applicationService.UpdateStatusAsync(publicId, adminId, request, cancellationToken);
        return result.IsSuccess
            ? NoContent()
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpPost("admin/{publicId:guid}/documents/{documentPublicId:guid}/review")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.AdminRole)]
    public async Task<IActionResult> ReviewDocument(
        Guid publicId,
        Guid documentPublicId,
        DocumentReviewDto request,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
        {
            return Unauthorized();
        }

        var result = await documentService.ReviewAsync(publicId, documentPublicId, adminId, request, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    [HttpGet("admin/{publicId:guid}/documents/{documentPublicId:guid}/download")]
    [Authorize(AuthenticationSchemes = ApiAuthenticationDefaults.Scheme, Roles = ApiAuthenticationDefaults.AdminRole)]
    public async Task<IActionResult> DownloadForAdmin(
        Guid publicId,
        Guid documentPublicId,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
        {
            return Unauthorized();
        }

        var result = await documentService.OpenForAdminAsync(publicId, documentPublicId, adminId, cancellationToken);
        return result.IsSuccess && result.Value is not null
            ? SecureDownload(result.Value)
            : Problem(statusCode: result.StatusCode, detail: result.Error);
    }

    private IActionResult SecureDownload(DocumentDownload download)
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(download.Content, download.ContentType, download.FileName, enableRangeProcessing: false);
    }
}
