using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit.Abstractions;

namespace GraduateApp.Tests;

public sealed class RealWebVisualQaProcessTests(ITestOutputHelper output)
{
    private const string ReadinessPath = "Account/AccessDenied";
    private static readonly HashSet<int> DevelopmentPorts = [5066, 7037, 7272];

    [Fact]
    public async Task Owned_real_web_process_uses_active_dll_and_releases_its_listener()
    {
        var artifact = WebBuildArtifact.FromTestOutput(AppContext.BaseDirectory);
        var port = GetUniqueLoopbackTestPort();
        Assert.DoesNotContain(port, DevelopmentPorts);

        var server = await OwnedWebProcess.StartAsync(artifact, port);
        output.WriteLine(
            "Owned Web PID={0}; loopback test port={1}; configuration={2}; TFM={3}; artifact=framework-dependent DLL.",
            server.ProcessId,
            port,
            artifact.Configuration,
            artifact.TargetFramework);

        try
        {
            using var response = await GetAsync(port, ReadinessPath);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(server.HasExited);
            Assert.EndsWith(
                "GraduateApp.Web.dll",
                artifact.DllPath,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await server.DisposeAsync();
        }

        Assert.True(server.HasExited);
        Assert.DoesNotContain("Unhandled exception", server.Output, StringComparison.OrdinalIgnoreCase);
        Assert.True(CanBind(port), $"Visual-QA listener portu serbest bırakılmadı: {port}");
    }

    [Fact]
    public void Active_web_artifact_uses_test_configuration_and_target_framework()
    {
        var testOutput = new DirectoryInfo(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)));
        var artifact = WebBuildArtifact.FromTestOutput(AppContext.BaseDirectory);

        Assert.Equal(testOutput.Name, artifact.TargetFramework);
        Assert.Equal(testOutput.Parent!.Name, artifact.Configuration);
        Assert.Equal(
            Path.Combine(
                artifact.ProjectDirectory,
                "bin",
                artifact.Configuration,
                artifact.TargetFramework,
                "GraduateApp.Web.dll"),
            artifact.DllPath);
        Assert.Equal(
            Path.Combine(artifact.RepositoryRoot, "GraduateApp.Web"),
            artifact.ProjectDirectory);
    }

    [Fact]
    public void Dotnet_host_prefers_valid_DOTNET_HOST_PATH_and_falls_back_safely()
    {
        var configuredHost = Path.Combine(
            Path.GetPathRoot(AppContext.BaseDirectory)!,
            "test-host",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        Assert.Equal(
            configuredHost,
            OwnedWebProcess.SelectDotNetHost(
                configuredHost,
                path => string.Equals(path, configuredHost, StringComparison.Ordinal)));
        Assert.Equal(
            "dotnet",
            OwnedWebProcess.SelectDotNetHost(configuredHost, _ => false));
        Assert.Equal(
            "dotnet",
            OwnedWebProcess.SelectDotNetHost(null, _ => true));
    }

    [Fact]
    public async Task Missing_dll_fails_with_controlled_artifact_diagnostic()
    {
        var activeArtifact = WebBuildArtifact.FromTestOutput(AppContext.BaseDirectory);
        var missingArtifact = activeArtifact with
        {
            DllPath = Path.Combine(
                activeArtifact.ProjectDirectory,
                "bin",
                activeArtifact.Configuration,
                activeArtifact.TargetFramework,
                "missing-GraduateApp.Web.dll")
        };
        var port = GetUniqueLoopbackTestPort();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OwnedWebProcess.StartAsync(missingArtifact, port));

        Assert.Contains($"port={port}", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            $"configuration={activeArtifact.Configuration}",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("artifact=framework-dependent DLL", exception.Message, StringComparison.Ordinal);
        Assert.Contains("bulunamadı", exception.Message, StringComparison.Ordinal);
        Assert.True(CanBind(port), $"Eksik artifact hatasından sonra port kullanılıyor: {port}");
    }

    [Fact]
    public async Task Failed_startup_cleans_only_its_owned_process_and_listener()
    {
        var artifact = WebBuildArtifact.FromTestOutput(AppContext.BaseDirectory);
        var survivorPort = GetUniqueLoopbackTestPort();
        var failingPort = GetUniqueLoopbackTestPort(new HashSet<int> { survivorPort });
        var survivor = await OwnedWebProcess.StartAsync(artifact, survivorPort);

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => OwnedWebProcess.StartAsync(
                    artifact,
                    failingPort,
                    readinessPath: $"__visual_qa_missing_{Guid.NewGuid():N}",
                    readinessTimeout: TimeSpan.FromMilliseconds(750)));

            Assert.Contains($"port={failingPort}", exception.Message, StringComparison.Ordinal);
            Assert.Contains(
                $"configuration={artifact.Configuration}",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Contains("artifact=framework-dependent DLL", exception.Message, StringComparison.Ordinal);
            Assert.True(CanBind(failingPort), $"Başarısız startup listener'ı serbest bırakılmadı: {failingPort}");

            using var survivorResponse = await GetAsync(survivorPort, ReadinessPath);
            Assert.Equal(HttpStatusCode.OK, survivorResponse.StatusCode);
            Assert.False(survivor.HasExited);
        }
        finally
        {
            await survivor.DisposeAsync();
        }

        Assert.True(survivor.HasExited);
        Assert.True(CanBind(survivorPort), $"Diğer Web listener'ı serbest bırakılmadı: {survivorPort}");
    }

    private static async Task<HttpResponseMessage> GetAsync(int port, string path)
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromSeconds(3)
        };
        return await client.GetAsync(path);
    }

    private static int GetUniqueLoopbackTestPort(IReadOnlySet<int>? additionalExcludedPorts = null)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            if (!DevelopmentPorts.Contains(port)
                && (additionalExcludedPorts is null || !additionalExcludedPorts.Contains(port)))
            {
                return port;
            }
        }

        throw new InvalidOperationException(
            "Development portlarından farklı benzersiz bir loopback test portu ayrılamadı.");
    }

    private static bool CanBind(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed record WebBuildArtifact(
        string RepositoryRoot,
        string ProjectDirectory,
        string Configuration,
        string TargetFramework,
        string DllPath)
    {
        public static WebBuildArtifact FromTestOutput(string testBaseDirectory)
        {
            var targetFrameworkDirectory = new DirectoryInfo(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(testBaseDirectory)));
            var configurationDirectory = targetFrameworkDirectory.Parent
                ?? throw InvalidTestOutput(testBaseDirectory);
            var binDirectory = configurationDirectory.Parent
                ?? throw InvalidTestOutput(testBaseDirectory);
            var testProjectDirectory = binDirectory.Parent
                ?? throw InvalidTestOutput(testBaseDirectory);
            var repositoryRoot = testProjectDirectory.Parent
                ?? throw InvalidTestOutput(testBaseDirectory);

            if (!string.Equals(binDirectory.Name, "bin", StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidTestOutput(testBaseDirectory);
            }

            var webProjectDirectory = Path.Combine(repositoryRoot.FullName, "GraduateApp.Web");
            var dllPath = Path.Combine(
                webProjectDirectory,
                "bin",
                configurationDirectory.Name,
                targetFrameworkDirectory.Name,
                "GraduateApp.Web.dll");
            return new WebBuildArtifact(
                repositoryRoot.FullName,
                webProjectDirectory,
                configurationDirectory.Name,
                targetFrameworkDirectory.Name,
                dllPath);
        }

        private static InvalidOperationException InvalidTestOutput(string testBaseDirectory)
        {
            return new InvalidOperationException(
                $"Aktif test output dizininden configuration/TFM türetilemedi: {testBaseDirectory}");
        }
    }

    private sealed class OwnedWebProcess : IAsyncDisposable
    {
        private readonly Process process;
        private readonly Task<string> standardOutput;
        private readonly Task<string> standardError;
        private bool disposed;
        private bool exited;

        private OwnedWebProcess(
            Process process,
            Task<string> standardOutput,
            Task<string> standardError)
        {
            this.process = process;
            this.standardOutput = standardOutput;
            this.standardError = standardError;
        }

        public bool HasExited => exited || (!disposed && process.HasExited);
        public int ProcessId => process.Id;
        public string Output { get; private set; } = string.Empty;

        public static async Task<OwnedWebProcess> StartAsync(
            WebBuildArtifact artifact,
            int port,
            string readinessPath = ReadinessPath,
            TimeSpan? readinessTimeout = null)
        {
            if (!File.Exists(artifact.DllPath))
            {
                throw StartupFailure(
                    artifact,
                    port,
                    "Aktif build configuration için Web DLL artifact'i bulunamadı.");
            }

            Process process;
            try
            {
                process = Process.Start(CreateStartInfo(artifact, port))
                    ?? throw new InvalidOperationException("Process.Start null döndürdü.");
            }
            catch (Exception exception)
            {
                throw StartupFailure(
                    artifact,
                    port,
                    "Web process başlatılamadı.",
                    exception);
            }

            var server = new OwnedWebProcess(
                process,
                process.StandardOutput.ReadToEndAsync(),
                process.StandardError.ReadToEndAsync());
            try
            {
                await server.WaitUntilReadyAsync(
                    port,
                    readinessPath,
                    readinessTimeout ?? TimeSpan.FromSeconds(15));
                return server;
            }
            catch (Exception exception)
            {
                await server.DisposeAsync();
                throw StartupFailure(
                    artifact,
                    port,
                    "Web process readiness doğrulaması başarısız oldu.",
                    exception);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // Process, HasExited kontrolü ile Kill çağrısı arasında doğal olarak kapanmış olabilir.
                }
            }

            await process.WaitForExitAsync();
            exited = true;
            Output = string.Join(
                Environment.NewLine,
                await standardOutput,
                await standardError);
            process.Dispose();
        }

        internal static string SelectDotNetHost(
            string? configuredHost,
            Func<string, bool>? fileExists = null)
        {
            fileExists ??= File.Exists;
            return !string.IsNullOrWhiteSpace(configuredHost)
                && Path.IsPathFullyQualified(configuredHost)
                && fileExists(configuredHost)
                    ? configuredHost
                    : "dotnet";
        }

        private static ProcessStartInfo CreateStartInfo(WebBuildArtifact artifact, int port)
        {
            var dotnetHost = SelectDotNetHost(
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"));
            var startInfo = new ProcessStartInfo(dotnetHost);
            startInfo.ArgumentList.Add(artifact.DllPath);
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add($"http://127.0.0.1:{port}");
            startInfo.WorkingDirectory = artifact.ProjectDirectory;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
            startInfo.Environment["Logging__LogLevel__Default"] = "Warning";
            return startInfo;
        }

        private static InvalidOperationException StartupFailure(
            WebBuildArtifact artifact,
            int port,
            string reason,
            Exception? innerException = null)
        {
            var message = string.Join(
                " ",
                reason,
                $"port={port};",
                $"configuration={artifact.Configuration};",
                $"TFM={artifact.TargetFramework};",
                "artifact=framework-dependent DLL.");
            return new InvalidOperationException(message, innerException);
        }

        private async Task WaitUntilReadyAsync(
            int port,
            string readinessPath,
            TimeSpan readinessTimeout)
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
                Timeout = TimeSpan.FromMilliseconds(500)
            };
            var deadline = DateTimeOffset.UtcNow.Add(readinessTimeout);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Visual-QA Web process readiness öncesi kapandı. ExitCode={process.ExitCode}.");
                }

                try
                {
                    using var response = await client.GetAsync(readinessPath);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return;
                    }
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or TaskCanceledException)
                {
                    // Sunucu startup sırasında henüz dinlemiyor olabilir.
                }

                await Task.Delay(50);
            }

            throw new TimeoutException("Visual-QA Web process readiness süresini aştı.");
        }
    }
}
