using GraduateApp.API.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GraduateApp.API.Infrastructure;

public interface IProductionReadinessProbe
{
    ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public interface IDataProtectionReadinessProbe
{
    ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public sealed class DefaultDataProtectionReadinessProbe(IHostEnvironment environment)
    : IDataProtectionReadinessProbe
{
    public ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(environment.IsDevelopment());
    }
}

public interface ISqlServerReadinessProbe
{
    ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public sealed class SqlServerReadinessProbe(
    IConfiguration configuration,
    IOptions<DatabaseTimeoutOptions> timeoutOptions) : ISqlServerReadinessProbe
{
    public async ValueTask<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var timeouts = timeoutOptions.Value;
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = Math.Min(timeouts.ConnectionSeconds, timeouts.ReadinessSeconds)
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeouts.ReadinessSeconds));
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(timeout.Token);
        return connection.State == System.Data.ConnectionState.Open;
    }
}

public sealed class SqlServerReadinessHealthCheck(ISqlServerReadinessProbe probe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await probe.IsReadyAsync(cancellationToken)
                ? HealthCheckResult.Healthy("SQL dependency is ready.")
                : HealthCheckResult.Unhealthy("SQL dependency is not ready.");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQL readiness check timed out.");
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("SQL dependency is not ready.");
        }
    }
}

public sealed class ProviderReadinessHealthCheck(
    IPrivateFileStorage storage,
    IFileMalwareScanner scanner,
    IDataProtectionReadinessProbe dataProtection,
    IHostEnvironment environment) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var storageReady = await IsProviderReadyAsync(storage, cancellationToken);
        var scannerReady = await IsProviderReadyAsync(scanner, cancellationToken);
        var dataProtectionReady = await IsDataProtectionReadyAsync(cancellationToken);
        return storageReady && scannerReady && dataProtectionReady
            ? HealthCheckResult.Healthy("Operational providers are configured.")
            : HealthCheckResult.Unhealthy("One or more operational providers are not ready.");
    }

    private async ValueTask<bool> IsProviderReadyAsync(
        object provider,
        CancellationToken cancellationToken)
    {
        if (provider is UnavailablePrivateFileStorage or UnavailableFileMalwareScanner)
        {
            return false;
        }

        if (provider is DevelopmentPrivateFileStorage or DevelopmentNoOpFileMalwareScanner)
        {
            return environment.IsDevelopment();
        }

        if (provider is not IProductionReadinessProbe probe)
        {
            return false;
        }

        try
        {
            return await probe.IsReadyAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async ValueTask<bool> IsDataProtectionReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dataProtection.IsReadyAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
