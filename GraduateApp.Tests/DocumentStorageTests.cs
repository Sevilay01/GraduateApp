using GraduateApp.API.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed class DocumentStorageTests
{
    [Fact]
    public async Task Development_storage_uses_opaque_keys_and_rejects_traversal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"graduateapp-storage-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var storage = new DevelopmentPrivateFileStorage(
                new TestHostEnvironment(root),
                Options.Create(new DocumentStorageOptions { DevelopmentRootPath = root }));
            await using var source = new MemoryStream("private"u8.ToArray());

            var key = await storage.SaveAsync(source, CancellationToken.None);

            Assert.Matches("^[a-f0-9]{32}$", key);
            Assert.True(await storage.ExistsAsync(key, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                storage.OpenReadAsync("../outside", CancellationToken.None));
            await using (var read = await storage.OpenReadAsync(key, CancellationToken.None))
            using (var reader = new StreamReader(read))
            {
                Assert.Equal("private", await reader.ReadToEndAsync());
            }
            await storage.DeleteAsync(key, CancellationToken.None);
            Assert.False(await storage.ExistsAsync(key, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "GraduateApp.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
