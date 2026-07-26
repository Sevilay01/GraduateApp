using System.Net;
using System.Security.Claims;
using System.Text.Json;
using GraduateApp.API.Controllers;
using GraduateApp.API.Infrastructure;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ApiCorrelationIdMiddleware = GraduateApp.API.Infrastructure.CorrelationIdMiddleware;

namespace GraduateApp.Tests;

public sealed class ProductionReadinessTests
{
    [Theory]
    [InlineData(2, 1)]
    [InlineData(8, 8)]
    public void Invalid_web_api_timeout_configuration_is_rejected_during_startup(
        int timeoutSeconds,
        int innerDependencyTimeoutSeconds)
    {
        using var factory = new InvalidWebTimeoutFactory(
            timeoutSeconds,
            innerDependencyTimeoutSeconds);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        var validation = FindException<OptionsValidationException>(exception);
        Assert.NotNull(validation);
        Assert.Contains(
            timeoutSeconds < 3 ? "TimeoutSeconds" : "Web API client timeout",
            validation.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3, 3, 3)]
    [InlineData(3, 4, 3)]
    [InlineData(4, 3, 3)]
    [InlineData(3, 5, 4)]
    [InlineData(5, 3, 4)]
    public void Invalid_database_timeout_value_matrix_is_rejected_by_bound_options_validation(
        int connectionSeconds,
        int commandSeconds,
        int readinessSeconds)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(DatabaseTimeoutSettings(5, 8, 3))
            .AddInMemoryCollection(DatabaseTimeoutSettings(
                connectionSeconds,
                commandSeconds,
                readinessSeconds))
            .Build();
        using var configurationLifetime = configuration as IDisposable;
        var services = new ServiceCollection();
        services.AddDatabaseTimeoutOptions(configuration);
        using var provider = services.BuildServiceProvider();

        var validation = Assert.Throws<OptionsValidationException>(() =>
            _ = provider
                .GetRequiredService<IOptions<DatabaseTimeoutOptions>>()
                .Value);

        Assert.Contains("Readiness timeout", validation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Database_timeout_test_overrides_take_precedence_and_are_accepted()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(DatabaseTimeoutSettings(5, 8, 3))
            .AddInMemoryCollection(DatabaseTimeoutSettings(4, 4, 3))
            .Build();
        using var configurationLifetime = configuration as IDisposable;
        var services = new ServiceCollection();
        services.AddDatabaseTimeoutOptions(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptions<DatabaseTimeoutOptions>>()
            .Value;

        Assert.Equal(4, options.ConnectionSeconds);
        Assert.Equal(4, options.CommandSeconds);
        Assert.Equal(3, options.ReadinessSeconds);
    }

    [Fact]
    public async Task Invalid_database_timeout_configuration_is_rejected_during_generic_host_startup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(DatabaseTimeoutSettings(3, 3, 3));
        builder.Services.AddDatabaseTimeoutOptions(builder.Configuration);
        using var host = builder.Build();

        var exception = await Record.ExceptionAsync(
            () => host.StartAsync(CancellationToken.None));

        Assert.NotNull(exception);
        var validation = FindException<OptionsValidationException>(exception);
        Assert.NotNull(validation);
        Assert.Contains("Readiness timeout", validation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Liveness_does_not_call_sql_or_operational_providers()
    {
        using var factory = new ReadinessApiFactory(replaceDataProtectionProbe: true);
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.SqlProbe.CallCount);
        Assert.Equal(0, factory.Storage.ReadinessCallCount);
        Assert.Equal(0, factory.Scanner.ReadinessCallCount);
        Assert.Equal(0, factory.DataProtectionProbe.ReadinessCallCount);
    }

    [Fact]
    public async Task Readiness_is_unhealthy_when_sql_is_unavailable_and_response_is_safe()
    {
        using var factory = new ReadinessApiFactory(sqlReady: false);
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Unhealthy", json.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("sensitive-host", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive-database", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive-password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Production_development_scanner_never_reports_ready()
    {
        using var factory = new ReadinessApiFactory(
            environmentName: Environments.Production,
            useDevelopmentScanner: true,
            replaceDataProtectionProbe: true);
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Production_without_explicit_data_protection_readiness_probe_never_reports_ready()
    {
        using var factory = new ReadinessApiFactory(
            environmentName: Environments.Production);
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Production_with_persistent_test_repository_but_without_explicit_readiness_probe_is_unhealthy()
    {
        using var repository = new TemporaryDirectory();
        var persistentTestProvider = DataProtectionProvider.Create(
            repository.DirectoryInfo,
            builder => builder.SetApplicationName("GraduateApp.API"));
        using var factory = new ReadinessApiFactory(
            environmentName: Environments.Production,
            dataProtectionProvider: persistentTestProvider);
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("PreProduction", false)]
    [InlineData("QA", false)]
    [InlineData("Test", false)]
    [InlineData("CustomEnvironment", false)]
    public async Task Default_data_protection_readiness_is_healthy_only_in_development(
        string environmentName,
        bool expected)
    {
        var probe = new DefaultDataProtectionReadinessProbe(
            new TestHostEnvironment { EnvironmentName = environmentName });

        var actual = await probe.IsReadyAsync(CancellationToken.None);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Valid_correlation_id_is_preserved_in_header_and_health_body()
    {
        const string correlationId = "graduate-test_2026.07-25";
        using var factory = new ReadinessApiFactory();
        using var client = factory.CreateClient(ClientOptions());
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(ApiCorrelationIdMiddleware.HeaderName, correlationId);

        using var response = await client.SendAsync(request);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(correlationId, response.Headers.GetValues(ApiCorrelationIdMiddleware.HeaderName).Single());
        Assert.Equal(correlationId, json.RootElement.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData("contains space")]
    [InlineData("contains/slash")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Invalid_correlation_id_is_replaced(string supplied)
    {
        using var factory = new ReadinessApiFactory();
        using var client = factory.CreateClient(ClientOptions());
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(ApiCorrelationIdMiddleware.HeaderName, supplied);

        using var response = await client.SendAsync(request);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var actual = response.Headers.GetValues(ApiCorrelationIdMiddleware.HeaderName).Single();

        Assert.NotEqual(supplied, actual);
        Assert.InRange(actual.Length, 1, 64);
        Assert.Equal(actual, json.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Problem_details_and_response_header_use_the_same_correlation_id()
    {
        using var factory = new ReadinessApiFactory();
        using var client = factory.CreateClient(ClientOptions());

        using var response = await client.GetAsync("/route-that-does-not-exist");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var header = response.Headers.GetValues(ApiCorrelationIdMiddleware.HeaderName).Single();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(header, json.RootElement.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(10928)]
    [InlineData(10929)]
    [InlineData(40143)]
    [InlineData(40197)]
    [InlineData(40501)]
    [InlineData(40540)]
    [InlineData(40613)]
    [InlineData(49918)]
    [InlineData(49919)]
    [InlineData(49920)]
    public async Task Authentication_database_unavailability_is_safe_503_not_401(int errorNumber)
    {
        using var factory = new ReadinessApiFactory(
            accessTokenService: new ThrowingAccessTokenService(
                TestSqlExceptionFactory.Create(errorNumber)));
        using var client = factory.CreateClient(ClientOptions());
        using var request = AuthorizedRequest("/api/students/me");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            "Servis geçici olarak kullanılamıyor. Lütfen kısa bir süre sonra tekrar deneyin.",
            json.RootElement.GetProperty("detail").GetString());
        Assert.Equal(
            response.Headers.GetValues(ApiCorrelationIdMiddleware.HeaderName).Single(),
            json.RootElement.GetProperty("correlationId").GetString());
        Assert.DoesNotContain("simulated", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unrecognized_sql_error_is_a_safe_500()
    {
        using var factory = new ReadinessApiFactory(
            accessTokenService: new ThrowingAccessTokenService(
                TestSqlExceptionFactory.Create(50000)));
        using var client = factory.CreateClient(ClientOptions());
        using var request = AuthorizedRequest("/api/students/me");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(
            "Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.",
            json.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain("simulated", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Web_login_preserves_authentication_database_outage_as_503()
    {
        using var apiResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                detail = "Servis geçici olarak kullanılamıyor. Lütfen kısa bir süre sonra tekrar deneyin."
            })
        };
        using var httpClient = new HttpClient(new StaticResponseHandler(apiResponse))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        var controller = new AccountController(new GraduateApiClient(httpClient))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.Login(new LoginViewModel
        {
            Username = "student@example.test",
            Password = "not-a-real-secret"
        }, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, controller.Response.StatusCode);
    }

    [Fact]
    public async Task Request_cancellation_is_not_mapped_to_auth_or_server_error_statuses()
    {
        using var factory = new ReadinessApiFactory(
            accessTokenService: new ThrowingAccessTokenService(
                new OperationCanceledException("simulated cancellation")));
        using var client = factory.CreateClient(ClientOptions());
        using var request = AuthorizedRequest("/api/students/me");

        using var response = await client.SendAsync(request);

        Assert.Equal(499, (int)response.StatusCode);
        Assert.False(new[] { 401, 409, 500, 503 }.Contains((int)response.StatusCode));
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    };

    private static Dictionary<string, string?> DatabaseTimeoutSettings(
        int connectionSeconds,
        int commandSeconds,
        int readinessSeconds) =>
        new()
        {
            ["DatabaseTimeouts:ConnectionSeconds"] =
                connectionSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["DatabaseTimeouts:CommandSeconds"] =
                commandSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["DatabaseTimeouts:ReadinessSeconds"] =
                readinessSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

    private static TException? FindException<TException>(Exception exception)
        where TException : Exception
    {
        if (exception is TException match)
        {
            return match;
        }

        if (exception is AggregateException aggregate)
        {
            return aggregate.InnerExceptions
                .Select(FindException<TException>)
                .FirstOrDefault(item => item is not null);
        }

        return exception.InnerException is null
            ? null
            : FindException<TException>(exception.InnerException);
    }

    private static HttpRequestMessage AuthorizedRequest(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            "opaque-test-token");
        return request;
    }

    private sealed class InvalidWebTimeoutFactory(
        int timeoutSeconds,
        int innerDependencyTimeoutSeconds)
        : WebApplicationFactory<AccountController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("GraduateApi:BaseAddress", "https://api.example.test/");
            builder.UseSetting(
                "GraduateApi:TimeoutSeconds",
                timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting(
                "GraduateApi:InnerDependencyTimeoutSeconds",
                innerDependencyTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            });
        }
    }

    private sealed class ReadinessApiFactory : WebApplicationFactory<ProgramsController>
    {
        private readonly string environmentName;
        private readonly bool useDevelopmentScanner;
        private readonly bool replaceDataProtectionProbe;
        private readonly IDataProtectionProvider? dataProtectionProvider;
        private readonly IAccessTokenService? accessTokenService;
        private readonly IReadOnlyList<KeyValuePair<string, string?>> configurationOverrides;

        public ReadinessApiFactory(
            bool sqlReady = true,
            string environmentName = "Testing",
            bool useDevelopmentScanner = false,
            bool replaceDataProtectionProbe = false,
            IDataProtectionProvider? dataProtectionProvider = null,
            IAccessTokenService? accessTokenService = null,
            IReadOnlyList<KeyValuePair<string, string?>>? configuration = null)
        {
            SqlProbe = new CountingSqlProbe(sqlReady);
            Storage = new ReadyStorage();
            Scanner = new ReadyScanner();
            DataProtectionProbe = new ReadyDataProtectionProbe();
            this.environmentName = environmentName;
            this.useDevelopmentScanner = useDevelopmentScanner;
            this.replaceDataProtectionProbe = replaceDataProtectionProbe;
            this.dataProtectionProvider = dataProtectionProvider;
            this.accessTokenService = accessTokenService;
            configurationOverrides = configuration ?? [];
        }

        public CountingSqlProbe SqlProbe { get; }
        public ReadyStorage Storage { get; }
        public ReadyScanner Scanner { get; }
        public ReadyDataProtectionProbe DataProtectionProbe { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            var testConfiguration = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=sensitive-host;Database=sensitive-database;User ID=sensitive-user;Password=sensitive-password;Encrypt=True"
            };
            foreach (var setting in configurationOverrides)
            {
                if (setting.Value is not null)
                {
                    testConfiguration[setting.Key] = setting.Value;
                }
            }

            // Minimal hosting reads some settings before app configuration callbacks run.
            // Seed bootstrap settings, then add the same values last for deterministic precedence.
            foreach (var setting in testConfiguration)
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                configurationBuilder.AddInMemoryCollection(testConfiguration));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton(
                    dataProtectionProvider ?? new EphemeralDataProtectionProvider());
                services.RemoveAll<ISqlServerReadinessProbe>();
                services.AddSingleton<ISqlServerReadinessProbe>(SqlProbe);
                services.RemoveAll<IPrivateFileStorage>();
                services.AddSingleton<IPrivateFileStorage>(Storage);
                services.RemoveAll<IFileMalwareScanner>();
                services.AddSingleton<IFileMalwareScanner>(
                    useDevelopmentScanner ? new DevelopmentNoOpFileMalwareScanner() : Scanner);
                if (replaceDataProtectionProbe)
                {
                    services.RemoveAll<IDataProtectionReadinessProbe>();
                    services.AddSingleton<IDataProtectionReadinessProbe>(DataProtectionProbe);
                }

                if (accessTokenService is not null)
                {
                    services.RemoveAll<IAccessTokenService>();
                    services.AddSingleton(accessTokenService);
                }
            });
        }
    }

    private sealed class CountingSqlProbe(bool ready) : ISqlServerReadinessProbe
    {
        public int CallCount { get; private set; }

        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ready);
        }
    }

    private sealed class ReadyStorage : IPrivateFileStorage, IProductionReadinessProbe
    {
        public int ReadinessCallCount { get; private set; }

        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            ReadinessCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public Task<string> SaveAsync(Stream source, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ReadyScanner : IFileMalwareScanner, IProductionReadinessProbe
    {
        public int ReadinessCallCount { get; private set; }

        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            ReadinessCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ReadyDataProtectionProbe : IDataProtectionReadinessProbe
    {
        public int ReadinessCallCount { get; private set; }

        public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            ReadinessCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }
    }

    private sealed class ThrowingAccessTokenService(Exception exception) : IAccessTokenService
    {
        public (string Token, DateTimeOffset ExpiresAtUtc) Issue(
            string subject,
            string role,
            string displayName,
            string securityStamp) =>
            throw new NotSupportedException();

        public Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken cancellationToken) =>
            Task.FromException<ClaimsPrincipal?>(exception);
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
