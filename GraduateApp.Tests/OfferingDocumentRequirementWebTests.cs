using System.Net;
using System.Text;
using System.Text.Json;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace GraduateApp.Tests;

public sealed class OfferingDocumentRequirementWebTests
{
    private const string RowVersion = "AQIDBAUGBwg=";
    private static readonly Guid RequirementPublicId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Offering_view_renders_the_correct_toggle_label_and_explicit_boolean_value()
    {
        var view = ReadOfferingView();

        Assert.Contains("asp-action=\"SetDocumentRequirementActive\"", view, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", view, StringComparison.Ordinal);
        Assert.Contains(
            "value=\"@((!requirement.IsActive).ToString().ToLowerInvariant())\"",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "@(requirement.IsActive ? \"Pasifleştir\" : \"Aktifleştir\")",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"@(!requirement.IsActive)\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Toggle_action_remains_post_only_and_antiforgery_protected()
    {
        var action = typeof(AdminController).GetMethod(nameof(AdminController.SetDocumentRequirementActive));

        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true).SingleOrDefault());
        Assert.NotNull(action.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).SingleOrDefault());
    }

    [Theory]
    [InlineData(false, "Belge koşulu pasifleştirildi.")]
    [InlineData(true, "Belge koşulu aktifleştirildi.")]
    public async Task Toggle_posts_the_requested_state_and_uses_the_persisted_state_for_success_message(
        bool requestedState,
        string expectedMessage)
    {
        var handler = new RequirementToggleHandler
        {
            PersistedIsActive = requestedState
        };
        var controller = CreateController(handler);

        var result = await controller.SetDocumentRequirementActive(
            42,
            RequirementPublicId,
            requestedState,
            RowVersion,
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminController.Offerings), redirect.ActionName);
        Assert.Equal("document-requirements", redirect.Fragment);
        Assert.Equal(42, redirect.RouteValues!["requirementOfferingId"]);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            $"/api/program-offerings/42/document-requirements/{RequirementPublicId:D}/active",
            handler.RequestUri!.AbsolutePath);
        using var request = JsonDocument.Parse(handler.RequestBody);
        Assert.Equal(requestedState, request.RootElement.GetProperty("isActive").GetBoolean());
        Assert.Equal(RowVersion, request.RootElement.GetProperty("rowVersion").GetString());
        Assert.Equal(expectedMessage, controller.TempData["SuccessMessage"]);
        Assert.False(controller.TempData.ContainsKey("ErrorMessage"));
    }

    [Fact]
    public async Task Success_message_uses_api_result_instead_of_the_posted_state()
    {
        var handler = new RequirementToggleHandler
        {
            PersistedIsActive = true
        };
        var controller = CreateController(handler);

        await controller.SetDocumentRequirementActive(
            42,
            RequirementPublicId,
            isActive: false,
            RowVersion,
            CancellationToken.None);

        Assert.Equal("Belge koşulu aktifleştirildi.", controller.TempData["SuccessMessage"]);
    }

    [Fact]
    public async Task Failed_api_response_does_not_create_success_message()
    {
        var handler = new RequirementToggleHandler
        {
            StatusCode = HttpStatusCode.Conflict,
            ErrorDetail = "Belge koşulu başka bir yönetici tarafından güncellendi."
        };
        var controller = CreateController(handler);

        await controller.SetDocumentRequirementActive(
            42,
            RequirementPublicId,
            isActive: true,
            RowVersion,
            CancellationToken.None);

        Assert.False(controller.TempData.ContainsKey("SuccessMessage"));
        Assert.Equal(handler.ErrorDetail, controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Invalid_toggle_value_does_not_call_api_or_create_success_message()
    {
        var handler = new RequirementToggleHandler();
        var controller = CreateController(handler);
        controller.ModelState.AddModelError("isActive", "Geçersiz boolean değer.");

        await controller.SetDocumentRequirementActive(
            42,
            RequirementPublicId,
            isActive: false,
            RowVersion,
            CancellationToken.None);

        Assert.Null(handler.Method);
        Assert.False(controller.TempData.ContainsKey("SuccessMessage"));
        Assert.Equal(
            "Belge koşulu durumu doğrulanamadı. Sayfayı yenileyip tekrar deneyin.",
            controller.TempData["ErrorMessage"]);
    }

    private static AdminController CreateController(RequirementToggleHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        return new AdminController(new GraduateApiClient(httpClient))
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new MemoryTempDataProvider())
        };
    }

    private static string ReadOfferingView()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repositoryRoot, "GraduateApp.Web", "Views", "Admin", "Offerings.cshtml"));
    }

    private sealed class RequirementToggleHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
        public bool PersistedIsActive { get; init; }
        public string ErrorDetail { get; init; } = "Belge koşulu güncellenemedi.";
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var responseBody = StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices
                ? JsonSerializer.Serialize(new
                {
                    publicId = RequirementPublicId,
                    isActive = PersistedIsActive,
                    rowVersion = "next"
                })
                : JsonSerializer.Serialize(new { detail = ErrorDetail });
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>(StringComparer.Ordinal);

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
