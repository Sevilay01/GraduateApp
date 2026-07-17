using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace GraduateApp.API.Models;

public sealed class GraduateAppDbContextFactory : IDesignTimeDbContextFactory<GraduateAppDbContext>
{
    public GraduateAppDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(ResolveConfigurationBasePath())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false);

        if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
        {
            configurationBuilder.AddUserSecrets<GraduateAppDbContextFactory>(optional: true);
        }

        var configuration = configurationBuilder
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection yapılandırılmamış. Değeri environment variable " +
                "(ConnectionStrings__DefaultConnection) veya Development ortamında user-secrets ile sağlayın.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<GraduateAppDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new GraduateAppDbContext(optionsBuilder.Options);
    }

    private static string ResolveConfigurationBasePath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(currentDirectory, "GraduateApp.API.csproj"))
            && File.Exists(Path.Combine(currentDirectory, "appsettings.json")))
        {
            return currentDirectory;
        }

        var projectDirectory = Path.Combine(currentDirectory, "GraduateApp.API");
        if (File.Exists(Path.Combine(projectDirectory, "appsettings.json")))
        {
            return projectDirectory;
        }

        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json")))
        {
            return AppContext.BaseDirectory;
        }

        throw new InvalidOperationException(
            "GraduateApp.API appsettings.json dosyası bulunamadı. EF komutunu solution veya API proje dizininden çalıştırın.");
    }
}
