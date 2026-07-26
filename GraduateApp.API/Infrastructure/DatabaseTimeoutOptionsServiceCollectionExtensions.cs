using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GraduateApp.API.Infrastructure;

public static class DatabaseTimeoutOptionsServiceCollectionExtensions
{
    public static IServiceCollection AddDatabaseTimeoutOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DatabaseTimeoutOptions>()
            .Bind(configuration.GetSection(DatabaseTimeoutOptions.SectionName))
            .Validate(
                options => options.ConnectionSeconds is >= 1 and <= 30,
                "DatabaseTimeouts:ConnectionSeconds 1 ile 30 arasında olmalıdır.")
            .Validate(
                options => options.CommandSeconds is >= 2 and <= 60,
                "DatabaseTimeouts:CommandSeconds 2 ile 60 arasında olmalıdır.")
            .Validate(
                options => options.ReadinessSeconds is >= 1 and <= 10,
                "DatabaseTimeouts:ReadinessSeconds 1 ile 10 arasında olmalıdır.")
            .Validate(
                options => options.ReadinessSeconds < options.ConnectionSeconds
                    && options.ReadinessSeconds < options.CommandSeconds,
                "Readiness timeout, SQL connection ve command timeout değerlerinden kısa olmalıdır.")
            .ValidateOnStart();

        return services;
    }
}
