using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed partial class StudentProfileGnoWebTests
{
    [Fact]
    public async Task Turkish_profile_render_uses_a_locale_friendly_gno_contract()
    {
        var handler = new ProfileApiHandler();
        using var factory = new StudentProfileWebFactory(handler);
        using var client = factory.CreateClient(ClientOptions());

        var session = await OpenProfileAsync(factory, client);
        var input = OpeningTag(session.Html, "<input", "name=\"Gno\"");
        var decodedInput = WebUtility.HtmlDecode(input);

        Assert.Contains("type=\"text\"", input, StringComparison.Ordinal);
        Assert.Contains("name=\"Gno\"", input, StringComparison.Ordinal);
        Assert.Contains("inputmode=\"decimal\"", input, StringComparison.Ordinal);
        Assert.Contains("value=\"3,60\"", input, StringComparison.Ordinal);
        Assert.Contains("data-val=\"true\"", input, StringComparison.Ordinal);
        Assert.Contains(
            "data-val-localizeddecimal=\"Lisans not ortalaması 0 ile 4 arasında geçerli bir ondalık sayı olmalıdır.\"",
            decodedInput,
            StringComparison.Ordinal);
        Assert.Contains("data-val-localizeddecimal-min=\"0\"", input, StringComparison.Ordinal);
        Assert.Contains("data-val-localizeddecimal-max=\"4\"", input, StringComparison.Ordinal);
        Assert.Contains("data-val-number=\"Geçerli bir sayı giriniz.\"", decodedInput, StringComparison.Ordinal);
        Assert.DoesNotContain("data-val-range", input, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", session.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("3,60")]
    [InlineData("3.60")]
    public async Task Turkish_and_canonical_gno_posts_bind_and_reach_the_api_exactly(
        string attemptedValue)
    {
        var handler = new ProfileApiHandler();
        using var factory = new StudentProfileWebFactory(handler);
        using var client = factory.CreateClient(ClientOptions());
        var session = await OpenProfileAsync(factory, client);

        using var response = await PostProfileAsync(client, session, attemptedValue);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Panel/Profile", response.Headers.Location?.OriginalString);
        var body = Assert.Single(handler.UpdateBodies);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(
            3.60m,
            document.RootElement.GetProperty("education").GetProperty("gno").GetDecimal());
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("4", "4")]
    [InlineData("", null)]
    public async Task Gno_boundaries_and_nullable_empty_value_follow_the_contract(
        string attemptedValue,
        string? expectedValue)
    {
        var handler = new ProfileApiHandler();
        using var factory = new StudentProfileWebFactory(handler);
        using var client = factory.CreateClient(ClientOptions());
        var session = await OpenProfileAsync(factory, client);

        using var response = await PostProfileAsync(client, session, attemptedValue);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var body = Assert.Single(handler.UpdateBodies);
        using var document = JsonDocument.Parse(body);
        var gno = document.RootElement.GetProperty("education").GetProperty("gno");
        if (expectedValue is null)
        {
            Assert.Equal(JsonValueKind.Null, gno.ValueKind);
        }
        else
        {
            Assert.Equal(
                decimal.Parse(expectedValue, CultureInfo.InvariantCulture),
                gno.GetDecimal());
        }
    }

    [Fact]
    public async Task Invalid_gno_values_never_reach_the_api_and_render_controlled_turkish_errors()
    {
        string[] invalidValues =
        [
            "-0,01",
            "4,01",
            "3,6,0",
            "3.6.0",
            "metin",
            "1,234.56",
            "1.234,56"
        ];
        var handler = new ProfileApiHandler();
        using var factory = new StudentProfileWebFactory(handler);
        using var client = factory.CreateClient(ClientOptions());
        var session = await OpenProfileAsync(factory, client);

        foreach (var invalidValue in invalidValues)
        {
            using var response = await PostProfileAsync(client, session, invalidValue);
            var html = await response.Content.ReadAsStringAsync();
            var decodedHtml = WebUtility.HtmlDecode(html);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(handler.UpdateBodies);
            Assert.Contains("Çukurova Üniversitesi", decodedHtml, StringComparison.Ordinal);
            Assert.Contains($"value=\"{invalidValue}\"", decodedHtml, StringComparison.Ordinal);
            Assert.True(
                decodedHtml.Contains(
                    "Lisans not ortalaması 0 ile 4 arasında geçerli bir ondalık sayı olmalıdır.",
                    StringComparison.Ordinal)
                || decodedHtml.Contains(
                    "Girilen değer geçerli bir sayı veya tarih biçiminde değil.",
                    StringComparison.Ordinal),
                $"Kontrollü Türkçe GNO hatası bulunamadı. Değer: {invalidValue}");
        }
    }

    [Fact]
    public async Task Invalid_post_preserves_the_university_catalog_failure_fallback()
    {
        var handler = new ProfileApiHandler();
        using var factory = new StudentProfileWebFactory(handler);
        using var client = factory.CreateClient(ClientOptions());
        var session = await OpenProfileAsync(factory, client);
        handler.FailUniversityCatalog = true;

        using var response = await PostProfileAsync(client, session, "3,6,0");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var gnoInput = OpeningTag(html, "<input", "name=\"Gno\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(handler.UpdateBodies);
        Assert.Contains("Üniversite kataloğu yüklenemedi.", html, StringComparison.Ordinal);
        Assert.Contains("disabled=\"disabled\"", gnoInput, StringComparison.Ordinal);
        Assert.Contains("value=\"3,6,0\"", gnoInput, StringComparison.Ordinal);
    }

    [Fact]
    public void Localized_decimal_adapter_is_loaded_and_scoped_to_marked_fields()
    {
        var repositoryRoot = RepositoryRoot();
        var partial = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "GraduateApp.Web",
            "Views",
            "Shared",
            "_ValidationScriptsPartial.cshtml"));
        var script = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "GraduateApp.Web",
            "wwwroot",
            "js",
            "localized-decimal-validation.js"));

        Assert.Contains("localized-decimal-validation.js", partial, StringComparison.Ordinal);
        Assert.Contains(
            "$.validator.unobtrusive.adapters.add(",
            script,
            StringComparison.Ordinal);
        Assert.Contains("\"localizeddecimal\"", script, StringComparison.Ordinal);
        Assert.Contains("options.rules.number = false;", script, StringComparison.Ordinal);
        Assert.Contains(
            "[\"min\", \"max\", \"scale\"]",
            script,
            StringComparison.Ordinal);
        Assert.Contains("fractionalDigitCount(value)", script, StringComparison.Ordinal);
        Assert.Contains("parameters.scale", script, StringComparison.Ordinal);
        Assert.Contains(
            "const decimalPattern = /^[+-]?(?:\\d+(?:[.,]\\d+)?|[.,]\\d+)$/;",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("$.validator.methods.number =", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$.validator.methods.range =", script, StringComparison.Ordinal);
    }

    private static async Task<ProfileSession> OpenProfileAsync(
        WebApplicationFactory<PanelController> factory,
        HttpClient client)
    {
        var authCookie = CreateStudentCookie(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Panel/Profile");
        request.Headers.Add("Cookie", authCookie);
        using var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var verificationToken = AntiforgeryTokenRegex().Match(html).Groups["token"].Value;
        Assert.False(string.IsNullOrWhiteSpace(verificationToken));
        var antiforgeryCookie = response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0])
            .Single(value => value.Contains("Antiforgery", StringComparison.Ordinal));
        return new ProfileSession(
            html,
            verificationToken,
            $"{authCookie}; {antiforgeryCookie}");
    }

    private static async Task<HttpResponseMessage> PostProfileAsync(
        HttpClient client,
        ProfileSession session,
        string gno)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Panel/Profile");
        request.Headers.Add("Cookie", session.CookieHeader);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = session.VerificationToken,
            ["FirstName"] = "Ada",
            ["LastName"] = "Lovelace",
            ["Email"] = "ada@example.test",
            ["Telephone"] = string.Empty,
            ["FatherName"] = string.Empty,
            ["BirthDate"] = string.Empty,
            ["UniversityId"] = "1",
            ["Faculty"] = "Mühendislik",
            ["GraduatedProgram"] = "Bilgisayar Mühendisliği",
            ["Gno"] = gno
        });
        return await client.SendAsync(request);
    }

    private static string CreateStudentCookie(
        WebApplicationFactory<PanelController> factory)
    {
        var cookieOptions = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "Ada Lovelace"),
                new Claim(ClaimTypes.Role, "Student"),
                new Claim(ApiSessionConstants.AccessTokenClaim, "student-token")
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

    private static string OpeningTag(string html, string tagStart, string marker)
    {
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"İşaret bulunamadı: {marker}");
        var start = html.LastIndexOf(tagStart, markerIndex, StringComparison.Ordinal);
        var end = html.IndexOf('>', markerIndex);
        Assert.True(start >= 0 && end > start);
        return html[start..(end + 1)];
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    };

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [GeneratedRegex(
        "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryTokenRegex();

    private sealed record ProfileSession(
        string Html,
        string VerificationToken,
        string CookieHeader);

    private sealed class StudentProfileWebFactory(ProfileApiHandler handler)
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
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        }
    }

    private sealed class ProfileApiHandler : HttpMessageHandler
    {
        private const string ProfileJson = """
            {
              "tcMasked": "100******46",
              "firstName": "Ada",
              "lastName": "Lovelace",
              "email": "ada@example.test",
              "telephone": null,
              "fatherName": null,
              "birthDate": null,
              "education": {
                "universityId": 1,
                "faculty": "Mühendislik",
                "graduatedProgram": "Bilgisayar Mühendisliği",
                "gno": 3.60
              }
            }
            """;

        public ConcurrentQueue<string> UpdateBodies { get; } = new();
        public bool FailUniversityCatalog { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/api/students/me")
            {
                return JsonResponse(ProfileJson);
            }

            if (request.Method == HttpMethod.Get && path == "/api/students/universities")
            {
                return FailUniversityCatalog
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent(
                            """{"detail":"Üniversite kataloğu geçici olarak kullanılamıyor."}""",
                            Encoding.UTF8,
                            "application/problem+json")
                    }
                    : JsonResponse(
                        """[{"universityId":1,"universityName":"Çukurova Üniversitesi"}]""");
            }

            if (request.Method == HttpMethod.Put && path == "/api/students/me")
            {
                UpdateBodies.Enqueue(
                    await request.Content!.ReadAsStringAsync(cancellationToken));
                return JsonResponse(ProfileJson);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }
}
