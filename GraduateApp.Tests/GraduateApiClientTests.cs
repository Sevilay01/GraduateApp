using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;

namespace GraduateApp.Tests;

public sealed class GraduateApiClientTests
{
    [Fact]
    public async Task Admin_login_sends_string_enum_account_type_in_request_body()
    {
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new GraduateApiClient(httpClient);

        await client.LoginAsync(new LoginViewModel
        {
            Username = "admin@example.test",
            Password = "Strong-Admin-1!",
            AccountType = LoginAccountType.Admin
        }, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("admin@example.test", body.RootElement.GetProperty("username").GetString());
        Assert.Equal("Admin", body.RootElement.GetProperty("accountType").GetString());
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("password").ValueKind);
    }

    [Fact]
    public void Web_and_api_login_account_type_values_are_identical()
    {
        Assert.Equal(
            (int)GraduateApp.API.Models.LoginAccountType.Student,
            (int)LoginAccountType.Student);
        Assert.Equal(
            (int)GraduateApp.API.Models.LoginAccountType.Admin,
            (int)LoginAccountType.Admin);
        Assert.Equal(
            GraduateApp.API.Models.LoginAccountType.Admin.ToString(),
            LoginAccountType.Admin.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Login_preserves_distinct_api_failure_status(HttpStatusCode statusCode)
    {
        using var httpClient = new HttpClient(new StubHandler(new HttpResponseMessage(statusCode)
        {
            Content = JsonContent.Create(new { detail = $"safe-{(int)statusCode}" })
        }))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new GraduateApiClient(httpClient);

        var result = await client.LoginAsync(new LoginViewModel
        {
            Username = "admin@example.test",
            Password = "Strong-Admin-1!",
            AccountType = LoginAccountType.Admin
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(statusCode, result.StatusCode);
        Assert.Equal($"safe-{(int)statusCode}", result.Error);
    }

    [Fact]
    public async Task FailedApiResponse_IsHandledWithoutDeserializingDomainPayload()
    {
        using var httpClient = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>internal details</html>", Encoding.UTF8, "text/html")
        }))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new GraduateApiClient(httpClient);

        var result = await client.GetOpenProgramsAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("İşlem tamamlanamadı. Lütfen daha sonra tekrar deneyin.", result.Error);
    }

    [Fact]
    public async Task Successful_api_response_with_empty_body_is_bad_gateway_failure()
    {
        using var httpClient = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        }))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new GraduateApiClient(httpClient);

        var result = await client.GetMyApplicationAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task Successful_api_response_with_json_null_body_is_bad_gateway_failure()
    {
        using var httpClient = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new GraduateApiClient(httpClient);

        var result = await client.GetMyApplicationAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task Successful_api_response_with_malformed_json_is_bad_gateway_failure()
    {
        using var httpClient = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new GraduateApiClient(httpClient);

        var result = await client.GetMyApplicationAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task Successful_api_response_with_unsupported_payload_is_bad_gateway_failure()
    {
        using var httpClient = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnsupportedContent()
        }))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new GraduateApiClient(httpClient);

        var result = await client.GetMyApplicationAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
    }

    [Fact]
    public async Task Create_offering_sends_null_row_version()
    {
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new GraduateApiClient(httpClient);

        await client.CreateProgramOfferingAsync(CreateOfferingForm(rowVersion: null), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("rowVersion").ValueKind);
    }

    [Fact]
    public async Task Update_offering_sends_existing_row_version()
    {
        const string rowVersion = "AQIDBA==";
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new GraduateApiClient(httpClient);
        var model = CreateOfferingForm(rowVersion);
        model.ProgramOfferingId = 42;

        await client.UpdateProgramOfferingAsync(model, CancellationToken.None);

        Assert.Equal(HttpMethod.Put, handler.Method);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(rowVersion, body.RootElement.GetProperty("rowVersion").GetString());
    }

    [Fact]
    public async Task Program_update_uses_typed_admin_route_and_preserves_row_version()
    {
        const string rowVersion = "AQIDBAUGBwg=";
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new GraduateApiClient(httpClient);

        await client.UpdateProgramAsync(new ProgramFormViewModel
        {
            ProgramId = 17,
            InstituteId = 3,
            ProgramName = "Bilgisayar Mühendisliği",
            DegreeType = "Tezli Yüksek Lisans",
            RowVersion = rowVersion
        }, CancellationToken.None);

        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal("api/admin/programs/17", handler.RequestUri!.PathAndQuery.TrimStart('/'));
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(3, body.RootElement.GetProperty("instituteId").GetInt32());
        Assert.Equal("Tezli Yüksek Lisans", body.RootElement.GetProperty("degreeType").GetString());
        Assert.Equal(rowVersion, body.RootElement.GetProperty("rowVersion").GetString());
        Assert.False(body.RootElement.TryGetProperty("isActive", out _));
    }

    [Fact]
    public async Task Institute_delete_sends_row_version_in_escaped_query_string()
    {
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var client = new GraduateApiClient(httpClient);

        await client.DeleteInstituteAsync(8, "AQIDBAUGBwg=", CancellationToken.None);

        Assert.Equal(HttpMethod.Delete, handler.Method);
        Assert.Equal("/api/admin/institutes/8?rowVersion=AQIDBAUGBwg%3D", handler.RequestUri!.PathAndQuery);
    }

    private static ProgramOfferingFormViewModel CreateOfferingForm(string? rowVersion) => new()
    {
        ProgramId = 1,
        AcademicYearStart = 2026,
        Term = AcademicTerm.Fall,
        ApplicationStartLocal = new DateTime(2026, 7, 1, 12, 0, 0),
        ApplicationDeadlineLocal = new DateTime(2026, 8, 1, 12, 0, 0),
        Quota = 10,
        RowVersion = rowVersion
    };

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class UnsupportedContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new NotSupportedException("The payload cannot be read as a stream."));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { detail = "Doğrulama başarısız." })
            };
        }
    }
}
