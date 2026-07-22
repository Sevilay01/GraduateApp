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

public sealed record ApiDownloadResult(
    bool IsSuccess,
    Stream? Content,
    string? ContentType,
    string? FileName,
    string? Error,
    HttpStatusCode? StatusCode)
{
    public static ApiDownloadResult Success(Stream content, string contentType, string fileName, HttpStatusCode statusCode) =>
        new(true, content, contentType, fileName, null, statusCode);

    public static ApiDownloadResult Failure(string error, HttpStatusCode? statusCode = null) =>
        new(false, null, null, null, error, statusCode);
}

internal sealed class ResponseOwnedStream(Stream inner, HttpResponseMessage response) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        inner.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.ReadAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            response.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        response.Dispose();
        GC.SuppressFinalize(this);
    }
}
