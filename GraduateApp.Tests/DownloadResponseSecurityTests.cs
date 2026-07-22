using System.Security.Claims;
using GraduateApp.API.Controllers;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Tests;

public sealed class DownloadResponseSecurityTests
{
    [Fact]
    public async Task Admin_download_uses_attachment_verified_type_nosniff_and_no_store()
    {
        var controller = new ApplicationsController(new UnusedApplicationService(), new DownloadDocumentService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "Admin")],
                        "test"))
                }
            }
        };

        var result = await controller.DownloadForAdmin(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("belge.pdf", file.FileDownloadName);
        Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
        await file.FileStream.DisposeAsync();
    }

    private sealed class DownloadDocumentService : IApplicationDocumentService
    {
        public Task<ServiceResult<DocumentDownload>> OpenForAdminAsync(
            Guid applicationPublicId,
            Guid documentPublicId,
            int adminId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ServiceResult<DocumentDownload>.Success(new DocumentDownload(
                new MemoryStream("%PDF-1.7"u8.ToArray()),
                "application/pdf",
                "belge.pdf")));

        public Task<ServiceResult<DocumentDownload>> OpenForStudentAsync(string studentTc, Guid applicationPublicId, Guid documentPublicId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServiceResult<ApplicationDocumentDto>> ReviewAsync(Guid applicationPublicId, Guid documentPublicId, int adminId, DocumentReviewDto request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServiceResult<ApplicationDocumentDto>> UploadAsync(string studentTc, Guid applicationPublicId, Guid requirementPublicId, IFormFile file, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedApplicationService : IApplicationService
    {
        public Task<ServiceResult<StudentApplicationDto>> CreateAsync(string studentTc, int programOfferingId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdminApplicationDetailDto?> GetDetailForAdminAsync(Guid publicId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StudentApplicationDetailDto?> GetDetailForStudentAsync(string studentTc, Guid publicId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DocumentWorkflowInvariantViolationDto>> GetDocumentWorkflowInvariantViolationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<AdminApplicationListItemDto>> GetForAdminAsync(string? search, ApplicationStatus? status, int? academicYearStart, AcademicTerm? term, int page, int pageSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StudentApplicationDto>> GetForStudentAsync(string studentTc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServiceResult> SubmitAsync(string studentTc, Guid publicId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServiceResult> UpdateStatusAsync(Guid publicId, int adminId, ApplicationStatusUpdateDto request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
