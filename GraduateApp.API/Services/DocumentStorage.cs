using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace GraduateApp.API.Services;

public sealed class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";
    public string? DevelopmentRootPath { get; init; }
}

public interface IPrivateFileStorage
{
    Task<string> SaveAsync(Stream source, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed partial class DevelopmentPrivateFileStorage : IPrivateFileStorage
{
    private readonly string rootPath;

    public DevelopmentPrivateFileStorage(
        IHostEnvironment environment,
        IOptions<DocumentStorageOptions> options)
    {
        rootPath = Path.GetFullPath(string.IsNullOrWhiteSpace(options.Value.DevelopmentRootPath)
            ? Path.Combine(environment.ContentRootPath, ".dev-storage")
            : options.Value.DevelopmentRootPath);
        Directory.CreateDirectory(rootPath);
    }

    public async Task<string> SaveAsync(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var objectKey = Guid.NewGuid().ToString("N");
        var destinationPath = Resolve(objectKey);
        var temporaryPath = Resolve($"{Guid.NewGuid():N}.tmp", allowTemporary: true);

        try
        {
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, 64 * 1024, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath, overwrite: false);
            return objectKey;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            Resolve(objectKey),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(Resolve(objectKey)));
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryDeleteFile(Resolve(objectKey));
        return Task.CompletedTask;
    }

    private string Resolve(string objectKey, bool allowTemporary = false)
    {
        var isValid = ObjectKeyPattern().IsMatch(objectKey)
            || (allowTemporary && TemporaryObjectKeyPattern().IsMatch(objectKey));
        if (!isValid)
        {
            throw new InvalidOperationException("Geçersiz depolama anahtarı.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, objectKey));
        var rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Geçersiz depolama anahtarı.");
        }

        return fullPath;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Compensation is best effort; callers record a storage audit event.
        }
        catch (UnauthorizedAccessException)
        {
            // Compensation is best effort; callers record a storage audit event.
        }
    }

    [GeneratedRegex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectKeyPattern();

    [GeneratedRegex("^[a-f0-9]{32}\\.tmp$", RegexOptions.CultureInvariant)]
    private static partial Regex TemporaryObjectKeyPattern();
}

public sealed class UnavailablePrivateFileStorage : IPrivateFileStorage
{
    private static InvalidOperationException Unavailable() =>
        new("Production özel dosya depolama sağlayıcısı yapılandırılmamış.");

    public Task<string> SaveAsync(Stream source, CancellationToken cancellationToken) =>
        Task.FromException<string>(Unavailable());

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromException<Stream>(Unavailable());

    public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromException<bool>(Unavailable());

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromException(Unavailable());
}
