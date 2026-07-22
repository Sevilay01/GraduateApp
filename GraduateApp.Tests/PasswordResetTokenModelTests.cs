using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GraduateApp.Tests;

public sealed class PasswordResetTokenModelTests
{
    [Fact]
    public void Sql_server_model_validation_does_not_emit_20601_for_purpose()
    {
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=GraduateAppModelValidation;Trusted_Connection=True;")
            .ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.BoolWithDefaultWarning))
            .Options;

        using var db = new GraduateAppDbContext(options);
        var property = db.Model.FindEntityType(typeof(PasswordResetToken))!
            .FindProperty(nameof(PasswordResetToken.Purpose))!;

        Assert.Equal(20601, RelationalEventId.BoolWithDefaultWarning.Id);
        Assert.Equal(default(PasswordResetTokenPurpose), property.Sentinel);
        Assert.Equal(PasswordResetTokenPurpose.PasswordReset, property.GetDefaultValue());
    }
}
