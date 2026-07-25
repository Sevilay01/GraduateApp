using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using GraduateApp.API.Controllers;
using GraduateApp.API.Domain;
using GraduateApp.API.Models;
using GraduateApp.API.Security;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed class StudentApplicationIdorResponseWebTests
{
    [Fact]
    public async Task Actual_api_preserves_owner_success_and_foreign_not_found_contract()
    {
        using var apiFactory = new StudentApplicationApiFactory();
        await apiFactory.SeedAsync();
        using var client = apiFactory.CreateClient(ClientOptions());

        using var ownerRequest = CreateApiRequest(
            apiFactory.OwnerApplicationPublicId,
            StudentApplicationApiFactory.OwnerToken);
        using var ownerResponse = await client.SendAsync(ownerRequest);
        var ownerHtml = await ownerResponse.Content.ReadAsStringAsync();

        using var foreignRequest = CreateApiRequest(
            apiFactory.OwnerApplicationPublicId,
            StudentApplicationApiFactory.OtherToken);
        using var foreignResponse = await client.SendAsync(foreignRequest);
        var foreignBody = await foreignResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        Assert.Contains("A Gizli Programı", ownerHtml, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Contains("Başvuru bulunamadı.", foreignBody, StringComparison.Ordinal);
        Assert.DoesNotContain("A Gizli Programı", foreignBody, StringComparison.Ordinal);
        Assert.DoesNotContain("a-gizli-belge.pdf", foreignBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Foreign_application_renders_controlled_web_404_without_losing_the_student_session()
    {
        using var apiFactory = new StudentApplicationApiFactory();
        await apiFactory.SeedAsync();
        using var apiClient = apiFactory.CreateClient(ClientOptions());
        var apiRequests = new ConcurrentQueue<string>();
        using var webFactory = new StudentPanelWebFactory(
            () => new ApiForwardingHandler(apiClient, apiRequests));
        using var client = webFactory.CreateClient(ClientOptions());
        var cookie = CreateStudentCookie(
            webFactory,
            StudentApplicationApiFactory.OtherToken);

        using var foreignRequest = CreateWebRequest(apiFactory.OwnerApplicationPublicId, cookie);
        using var foreignResponse = await client.SendAsync(foreignRequest);
        var foreignHtml = await foreignResponse.Content.ReadAsStringAsync();
        var decodedForeignHtml = WebUtility.HtmlDecode(foreignHtml);

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Contains("Başvuru bulunamadı.", decodedForeignHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("A Gizli Programı", decodedForeignHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("a-gizli-belge.pdf", decodedForeignHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            apiRequests,
            path => path.Contains("/published-evaluation", StringComparison.Ordinal));
        AssertAuthenticationCookieWasNotDeleted(foreignResponse);

        using var ownRequest = CreateWebRequest(apiFactory.OtherApplicationPublicId, cookie);
        using var ownResponse = await client.SendAsync(ownRequest);
        var ownHtml = await ownResponse.Content.ReadAsStringAsync();
        var decodedOwnHtml = WebUtility.HtmlDecode(ownHtml);

        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.Contains("B Öğrencisi Programı", decodedOwnHtml, StringComparison.Ordinal);
        Assert.Contains("b-transkript.pdf", decodedOwnHtml, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", ownHtml, StringComparison.Ordinal);
        Assert.Contains(
            $"/Panel/Applications/{apiFactory.OtherApplicationPublicId:D}/Documents/",
            ownHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            $"/Panel/Applications/{apiFactory.OtherApplicationPublicId:D}/Submit",
            ownHtml,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Actual_api_401_ends_cookie_once_and_redirects_to_anonymous_login_with_local_return_url()
    {
        using var apiFactory = new StudentApplicationApiFactory();
        await apiFactory.SeedAsync();
        using var apiClient = apiFactory.CreateClient(ClientOptions());
        using var webFactory = new StudentPanelWebFactory(
            () => new ApiForwardingHandler(apiClient, new ConcurrentQueue<string>()));
        using var client = webFactory.CreateClient(ClientOptions());
        var cookie = CreateStudentCookie(webFactory, "revoked-token");

        using var detailRequest = CreateWebRequest(apiFactory.OwnerApplicationPublicId, cookie);
        using var detailResponse = await client.SendAsync(detailRequest);

        Assert.Equal(HttpStatusCode.Redirect, detailResponse.StatusCode);
        Assert.NotNull(detailResponse.Headers.Location);
        var loginLocation = detailResponse.Headers.Location!.OriginalString;
        Assert.StartsWith("/Account/Login?", loginLocation, StringComparison.Ordinal);
        Assert.Contains("returnUrl=%2FPanel%2FApplications%2F", loginLocation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http%3A", loginLocation, StringComparison.OrdinalIgnoreCase);
        var authCookieDeletes = AuthenticationCookieHeaders(detailResponse).ToArray();
        Assert.Single(authCookieDeletes);
        Assert.Contains("expires=", authCookieDeletes[0], StringComparison.OrdinalIgnoreCase);

        using var loginResponse = await client.GetAsync(loginLocation);
        var loginHtml = await loginResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.DoesNotContain("Şu anda", loginHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("oturumunuz açık", loginHtml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Non_401_api_failures_preserve_status_and_cookie(HttpStatusCode apiStatus)
    {
        using var webFactory = new StudentPanelWebFactory(
            () => new StaticStatusHandler(apiStatus));
        using var client = webFactory.CreateClient(ClientOptions());
        var cookie = CreateStudentCookie(
            webFactory,
            StudentApplicationApiFactory.OtherToken);

        using var request = CreateWebRequest(Guid.NewGuid(), cookie);
        using var response = await client.SendAsync(request);

        Assert.Equal(apiStatus, response.StatusCode);
        AssertAuthenticationCookieWasNotDeleted(response);
    }

    [Fact]
    public async Task Api_timeout_is_service_unavailable_not_401_404_or_500_and_preserves_cookie()
    {
        using var webFactory = new StudentPanelWebFactory(
            static () => new TimeoutHandler());
        using var client = webFactory.CreateClient(ClientOptions());
        var cookie = CreateStudentCookie(
            webFactory,
            StudentApplicationApiFactory.OtherToken);

        using var request = CreateWebRequest(Guid.NewGuid(), cookie);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertAuthenticationCookieWasNotDeleted(response);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_becoming_an_http_failure()
    {
        using var httpClient = new HttpClient(new CancellationHandler())
        {
            BaseAddress = new Uri("https://api.example.test")
        };
        var apiClient = new GraduateApiClient(httpClient);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => apiClient.GetMyApplicationAsync(Guid.NewGuid(), cancellation.Token));
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    };

    private static HttpRequestMessage CreateApiRequest(Guid publicId, string token)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/applications/mine/{publicId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage CreateWebRequest(Guid publicId, string cookie)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/Panel/Applications/{publicId:D}");
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static string CreateStudentCookie(
        WebApplicationFactory<PanelController> factory,
        string accessToken)
    {
        var cookieOptions = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "Test Öğrencisi"),
                new Claim(ClaimTypes.Role, "Student"),
                new Claim(ApiSessionConstants.AccessTokenClaim, accessToken)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));
        var ticket = new AuthenticationTicket(
            principal,
            new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
            },
            CookieAuthenticationDefaults.AuthenticationScheme);
        return $"GraduateApp.Auth={cookieOptions.TicketDataFormat.Protect(ticket)}";
    }

    private static void AssertAuthenticationCookieWasNotDeleted(HttpResponseMessage response) =>
        Assert.Empty(AuthenticationCookieHeaders(response));

    private static IEnumerable<string> AuthenticationCookieHeaders(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(value => value.StartsWith("GraduateApp.Auth=", StringComparison.Ordinal))
            : [];

    private sealed class StudentPanelWebFactory(
        Func<HttpMessageHandler> primaryHandlerFactory)
        : WebApplicationFactory<PanelController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("GraduateApi:BaseAddress", "https://api.example.test");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddHttpClient<GraduateApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(primaryHandlerFactory);
            });
        }
    }

    private sealed class StudentApplicationApiFactory
        : WebApplicationFactory<ApplicationsController>
    {
        public const string OwnerToken = "student-a";
        public const string OtherToken = "student-b";
        private const string OwnerTc = "10000000146";
        private const string OtherTc = "10000000154";
        private readonly string databaseName = $"StudentApplicationApi_{Guid.NewGuid():N}";
        private bool seeded;

        public Guid OwnerApplicationPublicId { get; } = Guid.NewGuid();
        public Guid OtherApplicationPublicId { get; } = Guid.NewGuid();

        public async Task SeedAsync()
        {
            if (seeded)
            {
                return;
            }

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GraduateAppDbContext>();
            var now = new DateTime(2026, 7, 25, 9, 0, 0, DateTimeKind.Utc);
            db.Applications.Add(CreateApplication(
                OwnerApplicationPublicId,
                OwnerTc,
                "A Gizli Programı",
                "a-gizli-belge.pdf",
                now));
            db.Applications.Add(CreateApplication(
                OtherApplicationPublicId,
                OtherTc,
                "B Öğrencisi Programı",
                "b-transkript.pdf",
                now));
            await db.SaveChangesAsync();
            seeded = true;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(localdb)\\MSSQLLocalDB;Database=UnusedStudentApplicationApi;Integrated Security=true;Encrypt=false");
            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IDbContextOptionsConfiguration<GraduateAppDbContext>>();
                services.RemoveAll<DbContextOptions<GraduateAppDbContext>>();
                services.RemoveAll<GraduateAppDbContext>();
                services.AddDbContext<GraduateAppDbContext>(
                    options => options.UseInMemoryDatabase(databaseName));
                services.RemoveAll<IAccessTokenService>();
                services.AddSingleton<IAccessTokenService>(new FixedStudentAccessTokenService(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [OwnerToken] = OwnerTc,
                        [OtherToken] = OtherTc
                    }));
            });
        }

        private static Application CreateApplication(
            Guid publicId,
            string tc,
            string programName,
            string fileName,
            DateTime now)
        {
            var application = new Application
            {
                PublicId = publicId,
                TcNavigation = new Student
                {
                    Tc = tc,
                    PublicId = Guid.NewGuid(),
                    StudentName = "Test",
                    StudentSurname = "Öğrenci",
                    Email = $"{tc}@example.test",
                    NormalizedEmail = $"{tc}@EXAMPLE.TEST",
                    PasswordHash = "hash",
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                },
                ProgramOffering = new ProgramOffering
                {
                    Program = new GraduateApp.API.Models.Program
                    {
                        ProgramName = programName,
                        DegreeType = "Tezli Yüksek Lisans",
                        IsActive = true,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now,
                        Institute = new Institute
                        {
                            InstituteName = "Fen Bilimleri Enstitüsü",
                            IsActive = true,
                            CreatedAtUtc = now,
                            UpdatedAtUtc = now
                        }
                    },
                    AcademicYearStart = 2026,
                    Term = AcademicTerm.Fall,
                    ApplicationStartUtc = now.AddDays(-10),
                    ApplicationDeadlineUtc = now.AddDays(10),
                    Quota = 10,
                    IsOpen = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                },
                ApplicationDate = now,
                CurrentStatus = ApplicationStatus.Draft.ToString(),
                UsesDocumentWorkflow = true
            };
            var requirement = new ApplicationDocumentRequirementSnapshot
            {
                PublicId = Guid.NewGuid(),
                Application = application,
                DocumentCode = "TRANSCRIPT",
                DisplayName = "Transkript",
                IsRequired = true,
                AllowedContentCategory = DocumentContentCategory.PdfOnly,
                MaximumBytes = 1024 * 1024
            };
            var document = new ApplicationDocument
            {
                PublicId = Guid.NewGuid(),
                Application = application,
                RequirementSnapshot = requirement,
                VersionNumber = 1,
                IsCurrent = true,
                OriginalFileName = fileName,
                ObjectKey = Guid.NewGuid().ToString("N"),
                VerifiedContentType = "application/pdf",
                FileSize = 128,
                Sha256 = new string('a', 64),
                ReviewStatus = DocumentReviewStatus.Pending,
                UploadedAtUtc = now
            };
            requirement.Documents.Add(document);
            application.DocumentRequirementSnapshots.Add(requirement);
            application.Documents.Add(document);
            return application;
        }
    }

    private sealed class FixedStudentAccessTokenService(
        IReadOnlyDictionary<string, string> students) : IAccessTokenService
    {
        public (string Token, DateTimeOffset ExpiresAtUtc) Issue(
            string subject,
            string role,
            string displayName,
            string securityStamp) =>
            throw new NotSupportedException();

        public Task<ClaimsPrincipal?> ValidateAsync(
            string token,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!students.TryGetValue(token, out var tc))
            {
                return Task.FromResult<ClaimsPrincipal?>(null);
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, tc),
                    new Claim(ClaimTypes.Name, "Test Öğrencisi"),
                    new Claim(ClaimTypes.Role, ApiAuthenticationDefaults.StudentRole)
                ],
                ApiAuthenticationDefaults.Scheme));
            return Task.FromResult<ClaimsPrincipal?>(principal);
        }
    }

    private sealed class ApiForwardingHandler(
        HttpClient apiClient,
        ConcurrentQueue<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            requests.Enqueue(path);
            var forwardedRequest = new HttpRequestMessage(request.Method, path);
            foreach (var header in request.Headers)
            {
                forwardedRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return apiClient.SendAsync(forwardedRequest, cancellationToken);
        }
    }

    private sealed class StaticStatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("Simulated API timeout."));
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("A cancelled request must not reach the API transport.");
        }
    }
}
