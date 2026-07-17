using System.Net;

namespace GraduateApp.Web.Services;

public sealed record ApiResult<T>(bool IsSuccess, T? Value, string? Error, HttpStatusCode? StatusCode)
{
    public static ApiResult<T> Success(T value, HttpStatusCode statusCode) => new(true, value, null, statusCode);
    public static ApiResult<T> Failure(string error, HttpStatusCode? statusCode = null) => new(false, default, error, statusCode);
}

public sealed record ApiResult(bool IsSuccess, string? Error, HttpStatusCode? StatusCode)
{
    public static ApiResult Success(HttpStatusCode statusCode) => new(true, null, statusCode);
    public static ApiResult Failure(string error, HttpStatusCode? statusCode = null) => new(false, error, statusCode);
}
