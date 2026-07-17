using System.Net.Http.Headers;

namespace GraduateApp.Web.Services;

public sealed class ApiAccessTokenHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = httpContextAccessor.HttpContext?.User.FindFirst(ApiSessionConstants.AccessTokenClaim)?.Value;
        if (!string.IsNullOrWhiteSpace(token) && request.Headers.Authorization is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
