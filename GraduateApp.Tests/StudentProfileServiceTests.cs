using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;

namespace GraduateApp.Tests;

public sealed class StudentProfileServiceTests
{
    [Fact]
    public async Task Update_uses_registration_phone_normalization_and_rejects_canonical_duplicate()
    {
        await using var db = TestDb.Create();
        db.Students.AddRange(
            CreateStudent("10000000078", "one@example.test", "+905321234567"),
            CreateStudent("10000000214", "two@example.test", null));
        await db.SaveChangesAsync();
        var service = new StudentProfileService(db, TimeProvider.System);

        var result = await service.UpdateAsync(
            "10000000214",
            new UpdateStudentProfileDto
            {
                FirstName = "İki",
                LastName = "Öğrenci",
                Email = "two@example.test",
                Telephone = "0 (532) 123-45-67"
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Null(db.Students.Single(item => item.Tc == "10000000214").Telephone);
    }

    private static Student CreateStudent(string tc, string email, string? telephone) => new()
    {
        Tc = tc,
        StudentName = "Test",
        StudentSurname = "Öğrenci",
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        Telephone = telephone,
        PasswordHash = "hash",
        SecurityStamp = Guid.NewGuid().ToString("N")
    };
}
