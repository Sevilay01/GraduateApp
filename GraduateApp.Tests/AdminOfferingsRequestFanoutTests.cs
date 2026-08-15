using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Tests;

public sealed class AdminOfferingsRequestFanoutTests
{
    [Fact]
    public async Task Offering_list_uses_one_list_request_without_loading_catalog_or_subpage_collections()
    {
        var handler = new OfferingHandler();
        var controller = CreateController(handler);

        var result = Assert.IsType<ViewResult>(await controller.Offerings(
            academicYearStart: null,
            term: null,
            includeArchived: false,
            cancellationToken: CancellationToken.None));

        Assert.IsType<ProgramOfferingListPageViewModel>(result.Model);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/program-offerings", request.Path);
        Assert.Contains("summaryOnly=true", request.Query, StringComparison.Ordinal);
        Assert.DoesNotContain(handler.Requests, item => item.Path.Contains("/catalog", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, item => item.Path.Contains("document-requirements", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, item => item.Path.Contains("evaluations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Document_requirements_load_only_the_offering_header_and_its_requirements()
    {
        var handler = new OfferingHandler();
        var controller = CreateController(handler);

        var result = Assert.IsType<ViewResult>(await controller.DocumentRequirements(
            OfferingHandler.OfferingId,
            editRequirementId: null,
            CancellationToken.None));
        var model = Assert.IsType<OfferingDocumentRequirementsPageViewModel>(result.Model);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(handler.Requests, item => item.Path == $"/api/program-offerings/{OfferingHandler.OfferingId}");
        Assert.Contains(handler.Requests, item => item.Path == $"/api/program-offerings/{OfferingHandler.OfferingId}/document-requirements");
        Assert.Single(model.Requirements);
        Assert.DoesNotContain(handler.Requests, item => item.Path.Contains("evaluation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Legacy_edit_query_redirects_to_the_real_edit_route_without_api_fanout()
    {
        var handler = new OfferingHandler();
        var controller = CreateController(handler);

        var result = Assert.IsType<RedirectToActionResult>(await controller.Offerings(
            academicYearStart: null,
            term: null,
            includeArchived: false,
            editId: OfferingHandler.OfferingId,
            cancellationToken: CancellationToken.None));

        Assert.Equal(nameof(AdminController.EditOffering), result.ActionName);
        Assert.Equal(OfferingHandler.OfferingId, result.RouteValues!["id"]);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Legacy_document_query_preserves_the_selected_requirement_on_redirect()
    {
        var handler = new OfferingHandler();
        var controller = CreateController(handler);
        var requirementId = Guid.Parse("DC044726-758E-4C52-B028-A9AB744E3275");

        var result = Assert.IsType<RedirectToActionResult>(await controller.Offerings(
            academicYearStart: null,
            term: null,
            includeArchived: false,
            requirementOfferingId: OfferingHandler.OfferingId,
            editRequirementId: requirementId,
            cancellationToken: CancellationToken.None));

        Assert.Equal(nameof(AdminController.DocumentRequirements), result.ActionName);
        Assert.Equal(OfferingHandler.OfferingId, result.RouteValues!["id"]);
        Assert.Equal(requirementId, result.RouteValues["editRequirementId"]);
        Assert.Empty(handler.Requests);
    }

    private static AdminController CreateController(OfferingHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        return new AdminController(new GraduateApiClient(client));
    }

    private sealed class OfferingHandler : HttpMessageHandler
    {
        public const int OfferingId = 102;
        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue(new CapturedRequest(request.Method, path, request.RequestUri.Query));
            if (path.EndsWith("/document-requirements", StringComparison.Ordinal))
            {
                return Json<IReadOnlyList<OfferingDocumentRequirementViewModel>>(
                    [new()
                    {
                        PublicId = Guid.Parse("DC044726-758E-4C52-B028-A9AB744E3275"),
                        DocumentCode = "TRANSCRIPT",
                        DisplayName = "Transkript",
                        IsRequired = true,
                        IsActive = true,
                        MaximumBytes = 1024,
                        RowVersion = "cm93LXZlcnNpb24="
                    }]);
            }

            var offering = new ProgramOfferingAdminViewModel
            {
                ProgramOfferingId = OfferingId,
                ProgramId = 7,
                ProgramName = "Bilgisayar Mühendisliği",
                ProgramNameEnglish = "Computer Engineering",
                DegreeType = "Doktora",
                AcademicYearStart = 2026,
                AcademicYear = "2026–2027",
                Term = AcademicTerm.Fall,
                TermName = "Güz",
                UsesDocumentWorkflow = true,
                RowVersion = "AQIDBAUGBwg="
            };
            return path == "/api/program-offerings"
                ? Json<IReadOnlyList<ProgramOfferingAdminViewModel>>([offering])
                : Json(offering);
        }

        private static Task<HttpResponseMessage> Json<T>(T value) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value) });
    }

    private sealed record CapturedRequest(HttpMethod Method, string Path, string Query);
}
