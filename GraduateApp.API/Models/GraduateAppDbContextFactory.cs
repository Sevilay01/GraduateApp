using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GraduateApp.API.Models;

public sealed class GraduateAppDbContextFactory : IDesignTimeDbContextFactory<GraduateAppDbContext>
{
    public GraduateAppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GraduateAppDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            optionsBuilder.UseSqlServer();
        }
        else
        {
            optionsBuilder.UseSqlServer(connectionString);
        }

        return new GraduateAppDbContext(optionsBuilder.Options);
    }
}
