using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using GraduateApp.API.Models;
using GraduateApp.API.Security;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GraduateApp.Tests;

public sealed class ApiAccessTokenHandlerTests
{
    [Fact]
    public async Task Test_only_ephemeral_provider_recreation_invalidates_token_and_one_401_signs_out_once()
    {
        await using var db = TestDb.Create();
        var student = CreateStudent();
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var beforeRestart = new AccessTokenService(
            new EphemeralDataProtectionProvider(),
            db,
            clock);
        var afterRestart = new AccessTokenService(
            new EphemeralDataProtectionProvider(),
            db,
            clock);
        var issued = beforeRestart.Issue(
            student.Tc,
            ApiAuthenticationDefaults.StudentRole,
            "Display",
            student.SecurityStamp);

        var beforeRestartPrincipal = await beforeRestart.ValidateAsync(issued.Token, CancellationToken.None);
        var principal = await afterRestart.ValidateAsync(issued.Token, CancellationToken.None);
        var authentication = new RecordingAuthenticationService();
        await using var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(authentication)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ApiSessionConstants.AccessTokenClaim, issued.Token)],
                CookieAuthenticationDefaults.AuthenticationScheme))
        };
        using var handler = new ApiAccessTokenHandler(new HttpContextAccessor { HttpContext = httpContext })
        {
            InnerHandler = new AccessTokenValidationHandler(afterRestart)
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://localhost/api/students/me");

        Assert.NotNull(beforeRestartPrincipal);
        Assert.Null(principal);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, authentication.SignOutCount);
    }

    [Fact]
    public async Task Persistent_test_repository_with_the_same_application_name_preserves_the_api_token()
    {
        using var repository = new TemporaryDirectory();
        await using var db = TestDb.Create();
        var student = CreateStudent();
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var beforeRestart = new AccessTokenService(
            CreatePersistentTestProvider(repository, "GraduateApp.API"),
            db,
            clock);
        var issued = beforeRestart.Issue(
            student.Tc,
            ApiAuthenticationDefaults.StudentRole,
            "Display",
            student.SecurityStamp);
        var afterRestart = new AccessTokenService(
            CreatePersistentTestProvider(repository, "GraduateApp.API"),
            db,
            clock);

        var principal = await afterRestart.ValidateAsync(issued.Token, CancellationToken.None);

        Assert.NotNull(principal);
        Assert.Equal(student.Tc, principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public void Different_application_names_isolate_payloads_in_the_same_test_repository()
    {
        using var repository = new TemporaryDirectory();
        var apiProtector = CreatePersistentTestProvider(repository, "GraduateApp.API")
            .CreateProtector("shared-test-purpose");
        var webProtector = CreatePersistentTestProvider(repository, "GraduateApp.Web")
            .CreateProtector("shared-test-purpose");
        var protectedPayload = apiProtector.Protect("test-payload");

        Assert.Equal("test-payload", apiProtector.Unprotect(protectedPayload));
        Assert.Throws<CryptographicException>(() => webProtector.Unprotect(protectedPayload));
    }

    [Fact]
    public async Task Web_forwards_the_validated_correlation_id_to_the_api()
    {
        const string correlationId = "web-request_2026-07-25";
        var transport = new CorrelationCaptureHandler();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[CorrelationIdMiddleware.HeaderName] = correlationId;
        var middleware = new CorrelationIdMiddleware(
            async context =>
            {
                using var handler = new ApiAccessTokenHandler(
                    new HttpContextAccessor { HttpContext = context })
                {
                    InnerHandler = transport
                };
                using var client = new HttpClient(handler);
                using var response = await client.GetAsync(
                    "https://localhost/api/programs/open",
                    context.RequestAborted);
            },
            NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);

        Assert.Equal(correlationId, transport.CorrelationId);
        Assert.Equal(correlationId, httpContext.Response.Headers[CorrelationIdMiddleware.HeaderName]);
    }

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
    [InlineData(HttpStatusCode.ServiceUnavailable)]
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

    private static IDataProtectionProvider CreatePersistentTestProvider(
        TemporaryDirectory repository,
        string applicationName) =>
        DataProtectionProvider.Create(
            repository.DirectoryInfo,
            builder => builder.SetApplicationName(applicationName));

    private static Student CreateStudent()
    {
        var student = new Student
        {
            Tc = "10000000146",
            PublicId = Guid.NewGuid(),
            StudentName = "Test",
            StudentSurname = "Öğrenci",
            Email = "student@example.test",
            NormalizedEmail = "STUDENT@EXAMPLE.TEST",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        student.LoginIdentity = new LoginIdentity
        {
            NormalizedEmail = student.NormalizedEmail,
            AccountType = LoginAccountType.Student,
            Student = student,
            CreatedAtUtc = DateTime.UtcNow
        };
        return student;
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

    private sealed class AccessTokenValidationHandler(IAccessTokenService accessTokenService) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var token = request.Headers.Authorization?.Parameter;
            var principal = string.IsNullOrWhiteSpace(token)
                ? null
                : await accessTokenService.ValidateAsync(token, cancellationToken);
            return new HttpResponseMessage(
                principal is null ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
        }
    }

    private sealed class CorrelationCaptureHandler : HttpMessageHandler
    {
        public string? CorrelationId { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CorrelationId = request.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
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
