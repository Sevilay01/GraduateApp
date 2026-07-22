using GraduateApp.API.Domain;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed class DocumentFileValidatorTests
{
    public static TheoryData<byte[], string, string, DocumentContentCategory> ValidFiles => new()
    {
        { "%PDF-1.7\ncontent"u8.ToArray(), "belge.pdf", "application/pdf", DocumentContentCategory.PdfOnly },
        { [0xFF, 0xD8, 0xFF, 0xE0, 0x01], "fotoğraf.jpg", "image/jpeg", DocumentContentCategory.ImageOnly },
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01], "görsel.png", "image/png", DocumentContentCategory.PdfOrImage }
    };

    [Theory]
    [MemberData(nameof(ValidFiles))]
    public async Task Valid_signature_extension_and_content_type_are_accepted(
        byte[] content,
        string fileName,
        string contentType,
        DocumentContentCategory category)
    {
        var result = await CreateValidator().ValidateAsync(
            File(content, fileName, contentType),
            category,
            1024,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        await result.Value!.DisposeAsync();
    }

    [Fact]
    public async Task Executable_content_with_pdf_extension_is_rejected()
    {
        var result = await CreateValidator().ValidateAsync(
            File("MZ executable"u8.ToArray(), "not.pdf", "application/pdf"),
            DocumentContentCategory.PdfOnly,
            1024,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("gerçek içeriği", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Content_type_spoofing_is_rejected()
    {
        var result = await CreateValidator().ValidateAsync(
            File("%PDF-1.7"u8.ToArray(), "not.pdf", "image/png"),
            DocumentContentCategory.PdfOrImage,
            1024,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("uzantısı", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Traversal_name_is_reduced_to_safe_leaf_name()
    {
        var result = await CreateValidator().ValidateAsync(
            File("%PDF-1.7"u8.ToArray(), "../../secret/evil.pdf", "application/pdf"),
            DocumentContentCategory.PdfOnly,
            1024,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("evil.pdf", result.Value!.OriginalFileName);
        Assert.DoesNotContain("..", result.Value.OriginalFileName, StringComparison.Ordinal);
        await result.Value.DisposeAsync();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(11, 10)]
    public async Task Empty_and_over_limit_files_are_rejected(int length, int limit)
    {
        var result = await CreateValidator(maximumBytes: 100).ValidateAsync(
            File(new byte[length], "belge.pdf", "application/pdf"),
            DocumentContentCategory.PdfOnly,
            limit,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    private static DocumentFileValidator CreateValidator(long maximumBytes = 1024) =>
        new(Options.Create(new DocumentUploadOptions { MaximumBytes = maximumBytes }));

    private static FormFile File(byte[] content, string fileName, string contentType) => new(
        new MemoryStream(content),
        0,
        content.Length,
        "file",
        fileName)
    {
        Headers = new HeaderDictionary(),
        ContentType = contentType
    };
}
