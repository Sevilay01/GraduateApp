using System.Net;
using System.Reflection;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Tests;

public sealed class RegistrationWebContractTests
{
    [Fact]
    public async Task Invalid_form_is_not_sent_to_api_and_passwords_are_cleared()
    {
        var handler = new CountingHandler();
        var controller = new AccountController(new GraduateApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test")
        }));
        var model = new RegisterViewModel
        {
            Password = "Private-Password-1!",
            ConfirmPassword = "Private-Password-1!"
        };
        controller.ModelState.AddModelError(nameof(model.Tc), "Invalid");

        var result = await controller.Register(model, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(0, handler.RequestCount);
        Assert.Empty(model.Password);
        Assert.Empty(model.ConfirmPassword);
    }

    [Fact]
    public void Registration_post_requires_antiforgery_validation()
    {
        var action = typeof(AccountController).GetMethod(
            nameof(AccountController.Register),
            [typeof(RegisterViewModel), typeof(CancellationToken)]);

        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
    }

    [Fact]
    public async Task Api_error_preserves_non_secret_fields_but_clears_passwords()
    {
        var handler = new CountingHandler { StatusCode = HttpStatusCode.Conflict };
        var controller = new AccountController(new GraduateApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test")
        }));
        var model = new RegisterViewModel
        {
            Tc = "10000000078",
            FirstName = "İrem",
            LastName = "Öztürk",
            FatherName = "Ali",
            BirthDate = new DateOnly(2000, 1, 1),
            Email = "student@example.test",
            Telephone = "05321234567",
            Password = "Private-Password-1!",
            ConfirmPassword = "Private-Password-1!"
        };

        var result = await controller.Register(model, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("İrem", model.FirstName);
        Assert.Equal("05321234567", model.Telephone);
        Assert.Empty(model.Password);
        Assert.Empty(model.ConfirmPassword);
    }

    [Fact]
    public void Registration_view_contains_new_accessible_fields_and_validation_contract()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var view = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "GraduateApp.Web",
            "Views",
            "Account",
            "Register.cshtml"));

        Assert.Contains("asp-for=\"FatherName\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"BirthDate\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"Telephone\"", view, StringComparison.Ordinal);
        Assert.Contains("type=\"date\"", view, StringComparison.Ordinal);
        Assert.Contains("type=\"tel\"", view, StringComparison.Ordinal);
        Assert.Contains("inputmode=\"tel\"", view, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"tel\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-validation-summary", view, StringComparison.Ordinal);
        Assert.Contains("asp-validation-for=\"FatherName\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-validation-for=\"BirthDate\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-validation-for=\"Telephone\"", view, StringComparison.Ordinal);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.InternalServerError;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(StatusCode));
        }
    }
}
