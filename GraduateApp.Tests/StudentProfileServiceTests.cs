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
        var service = new StudentProfileService(db, TimeProvider.System, new InvariantEmailNormalizer());

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

    [Fact]
    public async Task Update_changes_student_and_central_identity_email_together()
    {
        await using var db = TestDb.Create();
        var student = CreateStudent("10000000078", "old@example.test", null);
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var service = new StudentProfileService(db, TimeProvider.System, new InvariantEmailNormalizer());

        var result = await service.UpdateAsync(student.Tc, new UpdateStudentProfileDto
        {
            FirstName = "Test",
            LastName = "Öğrenci",
            Email = "  New.Address@Example.Test  "
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("NEW.ADDRESS@EXAMPLE.TEST", student.NormalizedEmail);
        Assert.Equal(student.NormalizedEmail, student.LoginIdentity!.NormalizedEmail);
        Assert.Equal("New.Address@Example.Test", student.Email);
    }

    [Fact]
    public async Task Update_rejects_email_owned_by_admin_identity()
    {
        await using var db = TestDb.Create();
        var student = CreateStudent("10000000078", "student@example.test", null);
        var admin = new Admin
        {
            Email = "admin@example.test",
            NormalizedEmail = "ADMIN@EXAMPLE.TEST",
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        admin.LoginIdentity = new LoginIdentity
        {
            NormalizedEmail = admin.NormalizedEmail,
            AccountType = LoginAccountType.Admin,
            Admin = admin
        };
        db.AddRange(student, admin);
        await db.SaveChangesAsync();
        var service = new StudentProfileService(db, TimeProvider.System, new InvariantEmailNormalizer());

        var result = await service.UpdateAsync(student.Tc, new UpdateStudentProfileDto
        {
            FirstName = "Test",
            LastName = "Öğrenci",
            Email = " ADMIN@example.test "
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal("student@example.test", student.Email);
        Assert.Equal("STUDENT@EXAMPLE.TEST", student.LoginIdentity!.NormalizedEmail);
    }

    private static Student CreateStudent(string tc, string email, string? telephone)
    {
        var student = new Student
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
        student.LoginIdentity = new LoginIdentity
        {
            NormalizedEmail = student.NormalizedEmail,
            AccountType = LoginAccountType.Student,
            Student = student
        };
        return student;
    }
}
