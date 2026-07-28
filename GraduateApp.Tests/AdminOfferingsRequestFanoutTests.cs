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
    public static TheoryData<int> OfferingCounts => new()
    {
        0,
        1,
        25
    };

    [Theory]
    [MemberData(nameof(OfferingCounts))]
    public async Task Offering_list_outbound_call_count_is_constant_and_has_no_requirement_fan_out(
        int offeringCount)
    {
        var handler = new OfferingPageHandler(CreateOfferings(offeringCount));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var controller = new AdminController(new GraduateApiClient(client));

        var result = await controller.Offerings(
            academicYearStart: null,
            term: null,
            includeArchived: false,
            cancellationToken: CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path.Contains("/document-requirements", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Selected_offering_loads_its_full_requirements_once_without_loading_other_offerings()
    {
        var offerings = CreateOfferings(3);
        var selectedOfferingId = offerings[1].ProgramOfferingId;
        var handler = new OfferingPageHandler(offerings);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var controller = new AdminController(new GraduateApiClient(client));

        var result = Assert.IsType<ViewResult>(await controller.Offerings(
            academicYearStart: null,
            term: null,
            includeArchived: false,
            requirementOfferingId: selectedOfferingId,
            editRequirementId: Guid.Parse("DC044726-758E-4C52-B028-A9AB744E3275"),
            cancellationToken: CancellationToken.None));
        var model = Assert.IsType<ProgramOfferingPageViewModel>(result.Model);

        Assert.Equal(3, handler.Requests.Count);
        var requirementRequest = Assert.Single(
            handler.Requests,
            request => request.Path.Contains("/document-requirements", StringComparison.Ordinal));
        Assert.Equal(
            $"/api/program-offerings/{selectedOfferingId}/document-requirements",
            requirementRequest.Path);
        var selectedRequirements = Assert.Single(model.DocumentRequirements);
        Assert.Equal(selectedOfferingId, selectedRequirements.Key);
        Assert.Single(selectedRequirements.Value);
        Assert.Equal(selectedOfferingId, model.RequirementOfferingId);
        Assert.Equal(selectedOfferingId, model.DocumentRequirementForm.ProgramOfferingId);
        Assert.Equal("DOC-102", model.DocumentRequirementForm.DocumentCode);
        Assert.Equal(
            Guid.Parse("DC044726-758E-4C52-B028-A9AB744E3275"),
            model.DocumentRequirementForm.PublicId);
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path.Contains(
                $"/api/program-offerings/{offerings[0].ProgramOfferingId}/document-requirements",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path.Contains(
                $"/api/program-offerings/{offerings[2].ProgramOfferingId}/document-requirements",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Locked_evaluation_offering_loads_requirements_read_only_without_populating_an_edit_form()
    {
        var offerings = CreateOfferings(3);
        var selected = offerings[1];
        selected.UsesEvaluationWorkflow = true;
        selected.EvaluationState = OfferingEvaluationState.Published;
        var requirementPublicId = Guid.Parse("DC044726-758E-4C52-B028-A9AB744E3275");
        var handler = new OfferingPageHandler(offerings);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var controller = new AdminController(new GraduateApiClient(client));

        var result = Assert.IsType<ViewResult>(await controller.Offerings(
            academicYearStart: null,
            term: null,
            includeArchived: false,
            requirementOfferingId: selected.ProgramOfferingId,
            editRequirementId: requirementPublicId,
            cancellationToken: CancellationToken.None));
        var model = Assert.IsType<ProgramOfferingPageViewModel>(result.Model);

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.Single(model.DocumentRequirements[selected.ProgramOfferingId]);
        Assert.Equal(selected.ProgramOfferingId, model.RequirementOfferingId);
        Assert.Equal(0, model.DocumentRequirementForm.ProgramOfferingId);
        Assert.Equal(Guid.Empty, model.DocumentRequirementForm.PublicId);
        Assert.Null(model.DocumentRequirementForm.RowVersion);
    }

    [Fact]
    public async Task Invalid_requirement_offering_is_rejected_without_a_document_api_call()
    {
        var handler = new OfferingPageHandler(CreateOfferings(3));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var controller = new AdminController(new GraduateApiClient(client));

        var result = Assert.IsType<ViewResult>(await controller.Offerings(
            academicYearStart: null,
            term: null,
            includeArchived: false,
            requirementOfferingId: 999,
            cancellationToken: CancellationToken.None));
        var model = Assert.IsType<ProgramOfferingPageViewModel>(result.Model);

        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path.Contains("/document-requirements", StringComparison.Ordinal));
        Assert.Null(model.RequirementOfferingId);
        Assert.Equal(0, model.DocumentRequirementForm.ProgramOfferingId);
        Assert.Equal("Belge koşulları için seçilen ilan bulunamadı.", model.ErrorMessage);
    }

    [Fact]
    public async Task Edit_selection_populates_the_requested_offering_and_focus_without_loading_documents()
    {
        var offerings = CreateOfferings(3);
        var selected = offerings[2];
        var handler = new OfferingPageHandler(offerings);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var controller = new AdminController(new GraduateApiClient(client));

        var result = Assert.IsType<ViewResult>(await controller.Offerings(
            academicYearStart: null,
            term: null,
            includeArchived: false,
            editId: selected.ProgramOfferingId,
            cancellationToken: CancellationToken.None));
        var model = Assert.IsType<ProgramOfferingPageViewModel>(result.Model);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(selected.ProgramOfferingId, model.Form.ProgramOfferingId);
        Assert.Equal(selected.ProgramId, model.Form.ProgramId);
        Assert.Equal("offering-form-heading", model.AutoFocusTarget);
        Assert.Empty(model.DocumentRequirements);
    }

    [Fact]
    public async Task Unknown_edit_id_does_not_fall_through_to_another_offering()
    {
        var handler = new OfferingPageHandler(CreateOfferings(3));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var controller = new AdminController(new GraduateApiClient(client));

        var result = Assert.IsType<ViewResult>(await controller.Offerings(
            academicYearStart: null,
            term: null,
            includeArchived: false,
            editId: 999,
            cancellationToken: CancellationToken.None));
        var model = Assert.IsType<ProgramOfferingPageViewModel>(result.Model);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(0, model.Form.ProgramOfferingId);
        Assert.Equal("Düzenlenecek ilan bulunamadı.", model.ErrorMessage);
        Assert.Equal("offering-form-heading", model.AutoFocusTarget);
    }

    private static IReadOnlyList<ProgramOfferingAdminViewModel> CreateOfferings(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new ProgramOfferingAdminViewModel
            {
                ProgramOfferingId = 100 + index,
                ProgramId = 200 + index,
                ProgramName = $"Program {index}",
                AcademicYearStart = 2026,
                AcademicYear = "2026–2027",
                Term = AcademicTerm.Fall,
                TermName = "Güz",
                Quota = 10,
                RowVersion = Convert.ToBase64String(BitConverter.GetBytes(index))
            })
            .ToArray();

    private sealed class OfferingPageHandler(
        IReadOnlyList<ProgramOfferingAdminViewModel> offerings) : HttpMessageHandler
    {
        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue(new CapturedRequest(request.Method, path));

            if (path.EndsWith("/catalog", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, new ProgramOfferingCatalogViewModel());
            }

            if (path.EndsWith("/document-requirements", StringComparison.Ordinal))
            {
                var offeringId = int.Parse(
                    path.Split('/', StringSplitOptions.RemoveEmptyEntries)[2],
                    System.Globalization.CultureInfo.InvariantCulture);
                return Json(
                    HttpStatusCode.OK,
                    (IReadOnlyList<OfferingDocumentRequirementViewModel>)
                    [
                        new()
                        {
                            PublicId = Guid.Parse("DC044726-758E-4C52-B028-A9AB744E3275"),
                            DocumentCode = $"DOC-{offeringId}",
                            DisplayName = $"Belge {offeringId}",
                            IsRequired = true,
                            IsActive = true,
                            MaximumBytes = 1024,
                            RowVersion = "cm93LXZlcnNpb24="
                        }
                    ]);
            }

            return Json(HttpStatusCode.OK, offerings);
        }

        private static Task<HttpResponseMessage> Json<T>(HttpStatusCode statusCode, T value) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = JsonContent.Create(value)
            });
    }

    private sealed record CapturedRequest(HttpMethod Method, string Path);
}
