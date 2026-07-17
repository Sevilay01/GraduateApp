using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace GraduateApp.API.Security;

public sealed class ApiBearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAccessTokenService accessTokenService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            return AuthenticateResult.NoResult();
        }

        var principal = await accessTokenService.ValidateAsync(header.Parameter, Context.RequestAborted);
        if (principal is null)
        {
            return AuthenticateResult.Fail("Geçersiz veya süresi dolmuş erişim belirteci.");
        }

        return AuthenticateResult.Success(new AuthenticationTicket(principal, ApiAuthenticationDefaults.Scheme));
    }
}
