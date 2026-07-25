using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GraduateApp.API.Infrastructure;

public sealed class CorrelationProblemDetailsFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: ProblemDetails problemDetails })
        {
            problemDetails.Extensions[CorrelationIdMiddleware.ProblemDetailsExtensionName] =
                CorrelationIdMiddleware.Get(context.HttpContext);
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
