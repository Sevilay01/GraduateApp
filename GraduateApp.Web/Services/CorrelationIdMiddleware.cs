using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GraduateApp.Web.Services;

public sealed partial class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    private const string ItemKey = "GraduateApp.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveIncoming(context);
        context.Items[ItemKey] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await next(context);
        }
    }

    public static string? TryGet(HttpContext? context) =>
        context is not null
        && context.Items.TryGetValue(ItemKey, out var value)
        && value is string correlationId
            ? correlationId
            : null;

    private static string ResolveIncoming(HttpContext context)
    {
        var values = context.Request.Headers[HeaderName];
        return values.Count == 1
            && values[0] is { } candidate
            && ValidCorrelationIdPattern().IsMatch(candidate)
                ? candidate
                : ActivityTraceId.CreateRandom().ToHexString();
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidCorrelationIdPattern();
}
