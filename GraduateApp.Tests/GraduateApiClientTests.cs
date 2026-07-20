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

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { detail = "Doğrulama başarısız." })
            };
        }
    }
}
