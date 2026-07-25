using GraduateApp.API.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.API.Infrastructure;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    private static readonly EventId DatabaseUnavailable = new(1201, nameof(DatabaseUnavailable));
    private static readonly EventId UnexpectedFailure = new(1202, nameof(UnexpectedFailure));

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        if (exception is OperationCanceledException)
        {
            httpContext.Response.StatusCode = 499;
            return true;
        }

        ProblemDetails problem;
        if (DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            logger.LogWarning(
                DatabaseUnavailable,
                "A database dependency was unavailable while processing the request.");
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Servis geçici olarak kullanılamıyor.",
                Detail = "Servis geçici olarak kullanılamıyor. Lütfen kısa bir süre sonra tekrar deneyin."
            };
        }
        else
        {
            logger.LogError(
                UnexpectedFailure,
                "An unexpected request failure occurred. Exception type: {ExceptionType}.",
                exception.GetType().FullName);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "İşlem tamamlanamadı.",
                Detail = "Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin."
            };
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        _ = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem
        });
        return true;
    }
}
