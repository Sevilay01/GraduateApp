using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;
using DomainProgram = GraduateApp.API.Models.Program;

namespace GraduateApp.Tests;

public sealed class ApplicationServiceTests
{
    [Fact]
    public async Task Create_RejectsDuplicateApplication()
    {
        await using var db = TestDb.Create();
        var program = await SeedAsync(db, isOpen: true);
        var service = CreateService(db);

        var first = await service.CreateAsync("10000000146", program.ProgramId, CancellationToken.None);
        var duplicate = await service.CreateAsync("10000000146", program.ProgramId, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsClosedProgram()
    {
        await using var db = TestDb.Create();
        var program = await SeedAsync(db, isOpen: false);
        var service = CreateService(db);

        var result = await service.CreateAsync("10000000146", program.ProgramId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(db.Applications);
    }

    [Fact]
    public async Task StudentList_ReturnsOnlyAuthenticatedStudentsApplications()
    {
        await using var db = TestDb.Create();
        var program = await SeedAsync(db, isOpen: true);
        db.Students.Add(new Student
        {
            Tc = "10000000154",
            StudentName = "Diğer",
            StudentSurname = "Öğrenci",
            Email = "other@example.test",
            NormalizedEmail = "OTHER@EXAMPLE.TEST",
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString("N")
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await service.CreateAsync("10000000146", program.ProgramId, CancellationToken.None);
        await service.CreateAsync("10000000154", program.ProgramId, CancellationToken.None);

        var mine = await service.GetForStudentAsync("10000000146", CancellationToken.None);

        Assert.Single(mine);
        Assert.Equal("10000000146", db.Applications.Single(item => item.ApplicationId == mine[0].ApplicationId).Tc);
    }

    [Fact]
    public async Task UpdateStatus_RejectsInvalidTransition()
    {
        await using var db = TestDb.Create();
        var program = await SeedAsync(db, isOpen: true);
        var service = CreateService(db);
        var created = await service.CreateAsync("10000000146", program.ProgramId, CancellationToken.None);

        var result = await service.UpdateStatusAsync(
            created.Value!.ApplicationId,
            adminId: 1,
            new ApplicationStatusUpdateDto
            {
                NewStatus = ApplicationStatus.Approved,
                RowVersion = created.Value.RowVersion
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(ApplicationStatus.Pending.ToString(), db.Applications.Single().CurrentStatus);
    }

    private static ApplicationService CreateService(GraduateAppDbContext db) =>
        new(db, new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero)));

    private static async Task<DomainProgram> SeedAsync(GraduateAppDbContext db, bool isOpen)
    {
        var institute = new Institute { InstituteName = "Fen Bilimleri" };
        var program = new DomainProgram
        {
            ProgramName = "Bilgisayar Mühendisliği",
            DegreeType = "Tezli",
            IsOpen = isOpen,
            Institute = institute,
            ApplicationDeadlineUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Students.Add(new Student
        {
            Tc = "10000000146",
            StudentName = "Test",
            StudentSurname = "Öğrenci",
            Email = "student@example.test",
            NormalizedEmail = "STUDENT@EXAMPLE.TEST",
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString("N")
        });
        db.Programs.Add(program);
        await db.SaveChangesAsync();
        return program;
    }
}
