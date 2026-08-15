using System.Net;
using System.Text;
using System.Text.Json;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace GraduateApp.Tests;

public sealed class AdminCatalogToggleWebTests
{
    private const string RowVersion = "AQIDBAUGBwg=";

    [Fact]
    public void Institute_view_routes_active_and_inactive_records_to_explicit_actions()
    {
        var view = ReadView("Institutes.cshtml");
        var branch = view.IndexOf("@if (institute.IsActive)", StringComparison.Ordinal);
        var deactivate = view.IndexOf("asp-action=\"DeactivateInstitute\"", branch, StringComparison.Ordinal);
        var elseBranch = view.IndexOf("else", deactivate, StringComparison.Ordinal);
        var activate = view.IndexOf("asp-action=\"ActivateInstitute\"", elseBranch, StringComparison.Ordinal);

        Assert.True(branch >= 0);
        Assert.True(deactivate > branch);
        Assert.True(elseBranch > deactivate);
        Assert.True(activate > elseBranch);
        Assert.DoesNotContain("SetInstituteActive", view, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"isActive\"", view, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(view, "data-disable-on-submit=\"true\""));
    }

    [Fact]
    public void Program_view_routes_active_and_inactive_records_to_explicit_actions()
    {
        var view = ReadView("Programs.cshtml");
        var branch = view.IndexOf("@if (program.IsActive)", StringComparison.Ordinal);
        var deactivate = view.IndexOf("asp-action=\"DeactivateProgram\"", branch, StringComparison.Ordinal);
        var elseBranch = view.IndexOf("else", deactivate, StringComparison.Ordinal);
        var activate = view.IndexOf("asp-action=\"ActivateProgram\"", elseBranch, StringComparison.Ordinal);

        Assert.True(branch >= 0);
        Assert.True(deactivate > branch);
        Assert.True(elseBranch > deactivate);
        Assert.True(activate > elseBranch);
        Assert.DoesNotContain("SetProgramActive", view, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"isActive\"", view, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(view, "data-disable-on-submit=\"true\""));
    }

    [Fact]
    public Task Activate_institute_cannot_be_forced_to_deactivate_by_client_data() =>
        AssertFixedToggleAsync(
            nameof(AdminController.ActivateInstitute),
            "/api/admin/institutes/15/activate",
            nameof(AdminController.Institutes),
            controller => controller.ActivateInstitute(15, RowVersion, CancellationToken.None));

    [Fact]
    public Task Deactivate_institute_cannot_be_forced_to_activate_by_client_data() =>
        AssertFixedToggleAsync(
            nameof(AdminController.DeactivateInstitute),
            "/api/admin/institutes/15/deactivate",
            nameof(AdminController.Institutes),
            controller => controller.DeactivateInstitute(15, RowVersion, CancellationToken.None));

    [Fact]
    public Task Activate_program_cannot_be_forced_to_deactivate_by_client_data() =>
        AssertFixedToggleAsync(
            nameof(AdminController.ActivateProgram),
            "/api/admin/programs/15/activate",
            nameof(AdminController.Programs),
            controller => controller.ActivateProgram(15, RowVersion, CancellationToken.None));

    [Fact]
    public Task Deactivate_program_cannot_be_forced_to_activate_by_client_data() =>
        AssertFixedToggleAsync(
            nameof(AdminController.DeactivateProgram),
            "/api/admin/programs/15/deactivate",
            nameof(AdminController.Programs),
            controller => controller.DeactivateProgram(15, RowVersion, CancellationToken.None));

    [Fact]
    public async Task Remediation_close_posts_only_row_version_to_the_narrow_endpoint()
    {
        var handler = new ToggleHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var controller = new AdminController(new GraduateApiClient(httpClient))
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new MemoryTempDataProvider())
        };

        var result = Assert.IsType<RedirectToActionResult>(
            await controller.CloseInvalidOfferingForRemediation(
                programOfferingId: 15,
                rowVersion: RowVersion,
                academicYearStart: 2026,
                term: AcademicTerm.Fall,
                includeArchived: true,
                cancellationToken: CancellationToken.None));

        Assert.Equal(nameof(AdminController.OfferingOverview), result.ActionName);
        Assert.Equal(15, result.RouteValues!["id"]);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/program-offerings/15/close-for-remediation", handler.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.RequestBody);
        var property = Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal("rowVersion", property.Name);
        Assert.Equal(RowVersion, property.Value.GetString());
    }

    private static async Task AssertFixedToggleAsync(
        string actionName,
        string expectedApiPath,
        string expectedRedirectAction,
        Func<AdminController, Task<IActionResult>> invoke)
    {
        var action = typeof(AdminController).GetMethod(actionName);
        Assert.NotNull(action);
        Assert.DoesNotContain(action!.GetParameters(), parameter =>
            string.Equals(parameter.Name, "isActive", StringComparison.OrdinalIgnoreCase)
            || parameter.ParameterType == typeof(bool));

        var handler = new ToggleHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var controller = new AdminController(new GraduateApiClient(httpClient))
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new MemoryTempDataProvider())
        };

        var result = Assert.IsType<RedirectToActionResult>(await invoke(controller));

        Assert.Equal(expectedRedirectAction, result.ActionName);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(expectedApiPath, handler.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.RequestBody);
        var property = Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal("rowVersion", property.Name);
        Assert.Equal(RowVersion, property.Value.GetString());
    }

    private static string ReadView(string fileName)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repositoryRoot, "GraduateApp.Web", "Views", "Admin", fileName));
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private sealed class ToggleHandler : HttpMessageHandler
    {
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
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"rowVersion\":\"next\"}", Encoding.UTF8, "application/json")
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
