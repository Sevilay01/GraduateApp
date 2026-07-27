using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit.Abstractions;

namespace GraduateApp.Tests;

public sealed class RealWebVisualQaProcessTests(ITestOutputHelper output)
{
    private static readonly HashSet<int> DevelopmentPorts = [5066, 7037, 7272];

    [Fact]
    public async Task Owned_real_web_process_uses_unique_loopback_port_and_releases_its_listener()
    {
        var port = GetUniqueLoopbackTestPort();
        Assert.DoesNotContain(port, DevelopmentPorts);
        var server = await OwnedWebProcess.StartAsync(port);
        output.WriteLine(
            "Owned Web PID={0}; loopback test port={1}; development ports excluded.",
            server.ProcessId,
            port);

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
                Timeout = TimeSpan.FromSeconds(3)
            };
            using var response = await client.GetAsync("Account/AccessDenied");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(server.HasExited);
        }
        finally
        {
            await server.DisposeAsync();
        }

        Assert.True(server.HasExited);
        Assert.DoesNotContain("Unhandled exception", server.Output, StringComparison.OrdinalIgnoreCase);
        Assert.True(CanBind(port), $"Visual-QA listener portu serbest bırakılmadı: {port}");
    }

    private static int GetUniqueLoopbackTestPort()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            if (!DevelopmentPorts.Contains(port))
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

        public static async Task<OwnedWebProcess> StartAsync(int port)
        {
            var root = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                ".."));
            var webProjectDirectory = Path.Combine(root, "GraduateApp.Web");
            var webOutput = Path.Combine(webProjectDirectory, "bin", "Release", "net10.0");
            var startInfo = CreateStartInfo(webOutput, webProjectDirectory, port);
            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Visual-QA Web process başlatılamadı.");
            var server = new OwnedWebProcess(
                process,
                process.StandardOutput.ReadToEndAsync(),
                process.StandardError.ReadToEndAsync());
            try
            {
                await server.WaitUntilReadyAsync(port);
                return server;
            }
            catch
            {
                await server.DisposeAsync();
                throw;
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
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
            exited = true;
            Output = string.Join(
                Environment.NewLine,
                await standardOutput,
                await standardError);
            process.Dispose();
        }

        private static ProcessStartInfo CreateStartInfo(
            string webOutput,
            string webProjectDirectory,
            int port)
        {
            ProcessStartInfo startInfo;
            if (OperatingSystem.IsWindows())
            {
                startInfo = new ProcessStartInfo(
                    Path.Combine(webOutput, "GraduateApp.Web.exe"));
            }
            else
            {
                startInfo = new ProcessStartInfo("dotnet");
                startInfo.ArgumentList.Add(Path.Combine(webOutput, "GraduateApp.Web.dll"));
            }

            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add($"http://127.0.0.1:{port}");
            startInfo.WorkingDirectory = webProjectDirectory;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
            startInfo.Environment["Logging__LogLevel__Default"] = "Warning";
            return startInfo;
        }

        private async Task WaitUntilReadyAsync(int port)
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
                Timeout = TimeSpan.FromMilliseconds(500)
            };
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Visual-QA Web process readiness öncesi kapandı. ExitCode={process.ExitCode}.");
                }

                try
                {
                    using var response = await client.GetAsync("Account/AccessDenied");
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return;
                    }
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or TaskCanceledException)
                {
                    await Task.Delay(50);
                }
            }

            throw new TimeoutException("Visual-QA Web process readiness süresini aştı.");
        }
    }
}
