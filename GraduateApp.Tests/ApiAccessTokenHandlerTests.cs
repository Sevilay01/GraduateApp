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
        Assert.Equal(1, authentication.SignOutCount);
        Assert.False(httpContext.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task Concurrent_unauthorized_responses_end_the_same_web_session_once()
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

        var responses = await Task.WhenAll(
            client.GetAsync("https://localhost/api/students/me"),
            client.GetAsync("https://localhost/api/applications/mine"));
        using var first = responses[0];
        using var second = responses[1];

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(1, authentication.SignOutCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Non_unauthorized_api_responses_do_not_end_the_web_session(
        HttpStatusCode statusCode)
    {
        var authentication = new RecordingAuthenticationService();
        await using var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(authentication)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ApiSessionConstants.AccessTokenClaim, "current-token")],
                CookieAuthenticationDefaults.AuthenticationScheme))
        };
        using var handler = new ApiAccessTokenHandler(new HttpContextAccessor { HttpContext = httpContext })
        {
            InnerHandler = new StatusHandler(statusCode)
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://localhost/api/students/me");

        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(0, authentication.SignOutCount);
        Assert.True(httpContext.User.Identity?.IsAuthenticated);
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

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public string? SignedOutScheme { get; private set; }
        public int SignOutCount { get; private set; }

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
            SignOutCount++;
            return Task.CompletedTask;
        }
    }
}
