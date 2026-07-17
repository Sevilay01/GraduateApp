namespace GraduateApp.API.Services;

public sealed record ServiceResult<T>(bool IsSuccess, T? Value, string? Error, int StatusCode)
{
    public static ServiceResult<T> Success(T value, int statusCode = StatusCodes.Status200OK) =>
        new(true, value, null, statusCode);

    public static ServiceResult<T> Failure(string error, int statusCode) =>
        new(false, default, error, statusCode);
}

public sealed record ServiceResult(bool IsSuccess, string? Error, int StatusCode)
{
    public static ServiceResult Success(int statusCode = StatusCodes.Status204NoContent) =>
        new(true, null, statusCode);

    public static ServiceResult Failure(string error, int statusCode) =>
        new(false, error, statusCode);
}
