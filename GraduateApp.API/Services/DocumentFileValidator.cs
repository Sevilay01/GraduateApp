using System.Security.Cryptography;
using GraduateApp.API.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GraduateApp.API.Services;

public sealed class DocumentUploadOptions
{
    public const string SectionName = "DocumentUpload";
    public long MaximumBytes { get; init; } = DocumentWorkflowCatalog.DefaultMaximumUploadBytes;
}

public interface IDocumentFileValidator
{
    Task<ServiceResult<ValidatedDocumentFile>> ValidateAsync(
        IFormFile file,
        DocumentContentCategory allowedCategory,
        long requirementMaximumBytes,
        CancellationToken cancellationToken);
}

public sealed class ValidatedDocumentFile : IAsyncDisposable
{
    private readonly string temporaryPath;

    public ValidatedDocumentFile(
        string temporaryPath,
        string originalFileName,
        string verifiedContentType,
        long fileSize,
        string sha256)
    {
        this.temporaryPath = temporaryPath;
        OriginalFileName = originalFileName;
        VerifiedContentType = verifiedContentType;
        FileSize = fileSize;
        Sha256 = sha256;
    }

    public string OriginalFileName { get; }
    public string VerifiedContentType { get; }
    public long FileSize { get; }
    public string Sha256 { get; }

    public Stream OpenRead() => new FileStream(
        temporaryPath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        64 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    public ValueTask DisposeAsync()
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (FileNotFoundException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return ValueTask.CompletedTask;
    }
}

public sealed class DocumentFileValidator(IOptions<DocumentUploadOptions> options) : IDocumentFileValidator
{
    private static readonly IReadOnlyDictionary<string, string> ExtensionContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png"
        };

    public async Task<ServiceResult<ValidatedDocumentFile>> ValidateAsync(
        IFormFile file,
        DocumentContentCategory allowedCategory,
        long requirementMaximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var maximumBytes = Math.Min(options.Value.MaximumBytes, requirementMaximumBytes);
        if (maximumBytes <= 0)
        {
            return Invalid("Belge boyutu sınırı geçersiz.");
        }

        if (file.Length <= 0)
        {
            return Invalid("Boş dosya yüklenemez.");
        }

        if (file.Length > maximumBytes)
        {
            return Invalid($"Dosya izin verilen {FormatMegabytes(maximumBytes)} MB sınırını aşıyor.", StatusCodes.Status413PayloadTooLarge);
        }

        var originalFileName = SanitizeFileName(file.FileName);
        var extension = Path.GetExtension(originalFileName);
        if (!ExtensionContentTypes.TryGetValue(extension, out var extensionContentType))
        {
            return Invalid("Yalnızca PDF, JPEG veya PNG dosyaları yüklenebilir.");
        }

        var declaredContentType = file.ContentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (!string.Equals(extensionContentType, declaredContentType, StringComparison.Ordinal))
        {
            return Invalid("Dosya uzantısı ile bildirilen içerik türü uyuşmuyor.");
        }

        var temporaryPath = Path.Combine(Path.GetTempPath(), $"graduateapp-upload-{Guid.NewGuid():N}.tmp");
        try
        {
            long totalBytes = 0;
            var signature = new byte[16];
            var signatureLength = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = file.OpenReadStream())
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    totalBytes += read;
                    if (totalBytes > maximumBytes)
                    {
                        return Invalid($"Dosya izin verilen {FormatMegabytes(maximumBytes)} MB sınırını aşıyor.", StatusCodes.Status413PayloadTooLarge);
                    }

                    if (signatureLength < signature.Length)
                    {
                        var copyLength = Math.Min(read, signature.Length - signatureLength);
                        buffer.AsSpan(0, copyLength).CopyTo(signature.AsSpan(signatureLength));
                        signatureLength += copyLength;
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await output.FlushAsync(cancellationToken);
            }

            if (totalBytes == 0)
            {
                return Invalid("Boş dosya yüklenemez.");
            }

            var verifiedContentType = DetectContentType(signature.AsSpan(0, signatureLength));
            if (verifiedContentType is null
                || !string.Equals(verifiedContentType, extensionContentType, StringComparison.Ordinal)
                || !string.Equals(verifiedContentType, declaredContentType, StringComparison.Ordinal))
            {
                return Invalid("Dosyanın gerçek içeriği uzantısı veya bildirilen içerik türüyle uyuşmuyor.");
            }

            if (!allowedCategory.Allows(verifiedContentType))
            {
                return Invalid("Dosya türü bu belge koşulu için izin verilen kategoriye uygun değil.");
            }

            var result = new ValidatedDocumentFile(
                temporaryPath,
                originalFileName,
                verifiedContentType,
                totalBytes,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
            temporaryPath = string.Empty;
            return ServiceResult<ValidatedDocumentFile>.Success(result);
        }
        finally
        {
            if (!string.IsNullOrEmpty(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("%PDF-"u8))
        {
            return "application/pdf";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return bytes.StartsWith(pngSignature) ? "image/png" : null;
    }

    private static string SanitizeFileName(string suppliedName)
    {
        var leafName = Path.GetFileName((suppliedName ?? string.Empty).Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(leafName
            .Where(character => !char.IsControl(character) && !invalid.Contains(character))
            .ToArray())
            .Trim()
            .Trim('.');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "belge";
        }

        return sanitized.Length <= 255 ? sanitized : sanitized[..255];
    }

    private static string FormatMegabytes(long bytes) =>
        Math.Ceiling(bytes / (1024d * 1024d)).ToString("0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));

    private static ServiceResult<ValidatedDocumentFile> Invalid(
        string error,
        int statusCode = StatusCodes.Status400BadRequest) =>
        ServiceResult<ValidatedDocumentFile>.Failure(error, statusCode);
}
