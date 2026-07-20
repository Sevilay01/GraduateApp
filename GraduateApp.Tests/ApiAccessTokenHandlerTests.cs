using System.Net;
using System.Security.Claims;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GraduateApp.Tests;

public sealed class ApiAccessTokenHandlerTests
{
    [Fact]
    public async Task Unauthorized_api_response_signs_out_the_web_session()
    {
        var authentication = new RecordingAuthenticationService();
        await using var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(authentication)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ApiSessionConstants.AccessTokenClaim, "revoked-token")],
                CookieAuthenticationDefaults.AuthenticationScheme))
        };
        using var handler = new ApiAccessTokenHandler(new HttpContextAccessor { HttpContext = httpContext })
        {
            InnerHandler = new UnauthorizedHandler()
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://localhost/api/students/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.GetValues("X-Test-Authorization-Scheme").Single());
        Assert.Equal("revoked-token", response.Headers.GetValues("X-Test-Authorization-Parameter").Single());
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, authentication.SignedOutScheme);
    }

    private sealed class UnauthorizedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var authentication = request.Headers.Authorization;
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                RequestMessage = request
            };
            response.Headers.Add("X-Test-Authorization-Scheme", authentication?.Scheme ?? string.Empty);
            response.Headers.Add("X-Test-Authorization-Parameter", authentication?.Parameter ?? string.Empty);
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public string? SignedOutScheme { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            SignedOutScheme = scheme;
            return Task.CompletedTask;
        }
    }
}
