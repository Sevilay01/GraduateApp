using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace GraduateApp.Web.Services;

public sealed class ApiAccessTokenHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    private static readonly object SessionEndedKey = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var token = httpContext?.User.FindFirst(ApiSessionConstants.AccessTokenClaim)?.Value;
        if (!string.IsNullOrWhiteSpace(token) && request.Headers.Authorization is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized
            && TryEndCurrentSession(httpContext))
        {
            await httpContext!.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return response;
    }

    private static bool TryEndCurrentSession(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return false;
        }

        lock (httpContext)
        {
            if (httpContext.User.Identity?.IsAuthenticated != true
                || httpContext.Items.ContainsKey(SessionEndedKey))
            {
                return false;
            }

            httpContext.Items[SessionEndedKey] = true;
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
            return true;
        }
    }
}
