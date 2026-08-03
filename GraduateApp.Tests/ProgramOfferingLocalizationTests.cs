using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using GraduateApp.API.DTOs;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.Localization;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ApiAcademicTerm = GraduateApp.API.Domain.AcademicTerm;

namespace GraduateApp.Tests;

public sealed class ProgramOfferingLocalizationTests
{
    [Fact]
    public void New_offering_with_null_row_version_has_no_automatic_model_state_error()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore().AddDataAnnotations();
        using var serviceProvider = services.BuildServiceProvider();
        var objectValidator = serviceProvider.GetRequiredService<IObjectModelValidator>();
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());
        var model = CreateValidWebModel(rowVersion: null);

        objectValidator.Validate(actionContext, validationState: null, prefix: "Form", model);

        Assert.True(actionContext.ModelState.IsValid);
        Assert.DoesNotContain("Form.RowVersion", actionContext.ModelState.Keys);
    }

    [Fact]
    public async Task New_offering_with_null_row_version_reaches_create_api()
    {
        var handler = new OfferingPageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var controller = new AdminController(new GraduateApiClient(httpClient));

        var result = await controller.SaveOffering(CreateValidWebModel(rowVersion: null), CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var page = Assert.IsType<ProgramOfferingPageViewModel>(view.Model);
        Assert.Equal(1, handler.PostCount);
        Assert.DoesNotContain("Form.RowVersion", controller.ModelState.Keys);
        Assert.Equal("offering-form-heading", page.AutoFocusTarget);
        Assert.Equal(2026, page.AcademicYearStart);
        Assert.Equal(AcademicTerm.Fall, page.Term);
        Assert.Equal(1, page.Form.ProgramId);
    }

    [Fact]
    public async Task Update_with_empty_row_version_adds_turkish_error()
    {
        using var httpClient = new HttpClient(new OfferingPageHandler())
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var controller = new AdminController(new GraduateApiClient(httpClient));
        var model = CreateValidWebModel(rowVersion: " ");
        model.ProgramOfferingId = 42;

        var result = await controller.SaveOffering(model, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var page = Assert.IsType<ProgramOfferingPageViewModel>(view.Model);
        Assert.True(controller.ModelState.TryGetValue("Form.RowVersion", out var rowVersionState));
        var error = Assert.Single(rowVersionState.Errors);
        Assert.Equal("İlan eşzamanlılık bilgisi eksik. Sayfayı yenileyiniz.", error.ErrorMessage);
        Assert.Equal(42, page.Form.ProgramOfferingId);
        Assert.Equal("offering-form-heading", page.AutoFocusTarget);
    }

    [Fact]
    public async Task Successful_save_redirects_to_the_saved_offering_and_fragment()
    {
        var handler = new SuccessfulOfferingHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var controller = new AdminController(new GraduateApiClient(httpClient))
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new MemoryTempDataProvider())
        };
        var model = CreateValidWebModel(rowVersion: null);

        var result = Assert.IsType<RedirectToActionResult>(
            await controller.SaveOffering(model, CancellationToken.None));

        Assert.Equal(nameof(AdminController.Offerings), result.ActionName);
        Assert.Equal("offering-form", result.Fragment);
        Assert.Equal(73, result.RouteValues!["editId"]);
        Assert.Equal(2026, result.RouteValues["academicYearStart"]);
        Assert.Equal(AcademicTerm.Fall, result.RouteValues["term"]);
        Assert.Equal(false, result.RouteValues["includeArchived"]);
        Assert.Equal(1, handler.PostCount);
    }

    [Theory]
    [InlineData(AcademicTerm.LegacyUnspecified, "Belirtilmemiş")]
    [InlineData(AcademicTerm.Fall, "Güz")]
    [InlineData(AcademicTerm.Spring, "Bahar")]
    [InlineData(AcademicTerm.Summer, "Yaz")]
    public void Academic_terms_have_turkish_display_names(AcademicTerm term, string expected)
    {
        var member = Assert.Single(typeof(AcademicTerm).GetMember(term.ToString()));
        var display = member.GetCustomAttribute<DisplayAttribute>();

        Assert.NotNull(display);
        Assert.Equal(expected, display!.GetName());
    }

    [Fact]
    public void New_offering_dropdown_terms_exclude_legacy_value()
    {
        Assert.Equal(
            [AcademicTerm.Fall, AcademicTerm.Spring, AcademicTerm.Summer],
            AcademicTermDisplayExtensions.OfferingValues);
        Assert.Equal(
            ["Güz", "Bahar", "Yaz"],
            AcademicTermDisplayExtensions.OfferingValues.Select(term => term.DisplayName()));
    }

    [Fact]
    public void Offering_validation_messages_are_turkish()
    {
        var webForm = CreateValidWebModel(rowVersion: new string('x', 65));
        webForm.ProgramId = 0;
        webForm.AcademicYearStart = 1999;
        webForm.Term = (AcademicTerm)99;
        webForm.Quota = 0;
        var webMessages = Validate(webForm);

        Assert.Contains("Geçerli bir program seçiniz.", webMessages);
        Assert.Contains("Akademik yıl başlangıcı 2000 ile 2200 arasında olmalıdır.", webMessages);
        Assert.Contains("Geçerli bir dönem seçiniz.", webMessages);
        Assert.Contains("Kontenjan 1 ile 100000 arasında olmalıdır.", webMessages);
        Assert.Contains("İlan eşzamanlılık bilgisi geçersiz.", webMessages);
        Assert.Equal(
            "Başvuru başlangıç tarihi zorunludur.",
            Turkish(
                typeof(ProgramOfferingFormViewModel)
                    .GetProperty(nameof(ProgramOfferingFormViewModel.ApplicationStartLocal))!
                    .GetCustomAttribute<RequiredAttribute>()!
                    .ErrorMessage));
        Assert.Equal(
            "Son başvuru tarihi zorunludur.",
            Turkish(
                typeof(ProgramOfferingFormViewModel)
                    .GetProperty(nameof(ProgramOfferingFormViewModel.ApplicationDeadlineLocal))!
                    .GetCustomAttribute<RequiredAttribute>()!
                    .ErrorMessage));

        var apiCreate = new ProgramOfferingCreateDto
        {
            ProgramId = 0,
            AcademicYearStart = 1999,
            Term = (ApiAcademicTerm)99,
            ApplicationStartUtc = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            ApplicationDeadlineUtc = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc),
            Quota = 0
        };
        var apiCreateMessages = Validate(apiCreate);

        Assert.Contains("Geçerli bir program seçiniz.", apiCreateMessages);
        Assert.Contains("Akademik yıl başlangıcı 2000 ile 2200 arasında olmalıdır.", apiCreateMessages);
        Assert.Contains("Geçerli bir dönem seçiniz.", apiCreateMessages);
        Assert.Contains("Kontenjan 1 ile 100000 arasında olmalıdır.", apiCreateMessages);

        var apiUpdate = new ProgramOfferingUpdateDto
        {
            ProgramId = 1,
            AcademicYearStart = 2026,
            Term = ApiAcademicTerm.Fall,
            ApplicationStartUtc = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            ApplicationDeadlineUtc = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc),
            Quota = 10,
            RowVersion = string.Empty
        };

        Assert.Contains("İlan eşzamanlılık bilgisi zorunludur.", Validate(apiUpdate));

        var webRequirement = new ProgramOfferingRequirementInputViewModel
        {
            ExamId = 0,
            MinimumScore = 1000m
        };
        var apiRequirement = new ProgramOfferingRequirementInputDto
        {
            ExamId = 0,
            MinimumScore = 1000m
        };

        Assert.Contains("Geçerli bir sınav seçiniz.", Validate(webRequirement));
        Assert.Contains("Puan 0 ile 999,99 arasında olmalıdır.", Validate(webRequirement));
        Assert.Contains("Geçerli bir sınav seçiniz.", Validate(apiRequirement));
        Assert.Contains("Puan 0 ile 999,99 arasında olmalıdır.", Validate(apiRequirement));
    }

    private static ProgramOfferingFormViewModel CreateValidWebModel(string? rowVersion) => new()
    {
        ProgramId = 1,
        AcademicYearStart = 2026,
        Term = AcademicTerm.Fall,
        ApplicationStartLocal = new DateTime(2026, 7, 1, 12, 0, 0),
        ApplicationDeadlineLocal = new DateTime(2026, 8, 1, 12, 0, 0),
        Quota = 10,
        RowVersion = rowVersion
    };

    private static IReadOnlyList<string?> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results
            .Select(result => Turkish(result.ErrorMessage))
            .ToArray();
    }

    private static string? Turkish(string? keyOrText) =>
        keyOrText is null
            ? null
            : UiText.Get(new DefaultHttpContext(), keyOrText);

    private sealed class OfferingPageHandler : HttpMessageHandler
    {
        public int PostCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                PostCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = JsonContent.Create(new { detail = "Doğrulama başarısız." })
                });
            }

            HttpContent content = request.RequestUri!.AbsolutePath.EndsWith("/catalog", StringComparison.Ordinal)
                ? JsonContent.Create(new ProgramOfferingCatalogViewModel())
                : JsonContent.Create(Array.Empty<ProgramOfferingAdminViewModel>());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class SuccessfulOfferingHandler : HttpMessageHandler
    {
        public int PostCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            PostCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ProgramOfferingAdminViewModel
                {
                    ProgramOfferingId = 73,
                    ProgramId = 1,
                    ProgramName = "Bilgisayar Mühendisliği",
                    AcademicYearStart = 2026,
                    AcademicYear = "2026–2027",
                    Term = AcademicTerm.Fall,
                    TermName = "Güz",
                    Quota = 10,
                    IsOpen = false,
                    RowVersion = "AQIDBA=="
                })
            });
        }
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object?> values = new(StringComparer.Ordinal);

        public IDictionary<string, object?> LoadTempData(HttpContext context) =>
            new Dictionary<string, object?>(values, StringComparer.Ordinal);

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) =>
            this.values = new Dictionary<string, object?>(values, StringComparer.Ordinal);
    }
}
