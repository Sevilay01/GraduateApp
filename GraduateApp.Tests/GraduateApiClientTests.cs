using System.Net;
using System.Text;
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

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
