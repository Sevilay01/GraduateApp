using System.ComponentModel.DataAnnotations;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Http;

namespace GraduateApp.Tests;

public sealed class StudentExamScoreServiceTests
{
    [Fact]
    public async Task Student_can_list_create_update_and_delete_only_own_scores()
    {
        await using var db = TestDb.Create();
        var (owner, other, ales, _) = await SeedAsync(db);
        var service = CreateService(db);

        var created = await service.CreateAsync(owner.Tc, Request(ales.ExamId, 75, "2026-01-01"), CancellationToken.None);
        var mine = await service.GetForStudentAsync(owner.Tc, CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Single(mine);
        Assert.Equal(owner.Tc, db.StudentExamScores.Single().Tc);

        var foreignUpdate = await service.UpdateAsync(
            other.Tc,
            created.Value!.ScoreId,
            Request(ales.ExamId, 90, "2026-02-01"),
            CancellationToken.None);
        var foreignDelete = await service.DeleteAsync(other.Tc, created.Value.ScoreId, CancellationToken.None);

        Assert.False(foreignUpdate.IsSuccess);
        Assert.Equal(StatusCodes.Status404NotFound, foreignUpdate.StatusCode);
        Assert.False(foreignDelete.IsSuccess);
        Assert.Equal(StatusCodes.Status404NotFound, foreignDelete.StatusCode);
        Assert.Equal(75, db.StudentExamScores.Single().Score);

        var updated = await service.UpdateAsync(
            owner.Tc,
            created.Value.ScoreId,
            Request(ales.ExamId, 90, "2026-02-01"),
            CancellationToken.None);
        var deleted = await service.DeleteAsync(owner.Tc, created.Value.ScoreId, CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Equal(90, updated.Value!.Score);
        Assert.True(deleted.IsSuccess);
        Assert.Empty(db.StudentExamScores);
    }

    [Theory]
    [InlineData("ALES")]
    [InlineData("e-YDS")]
    [InlineData("YÖKDİL")]
    public async Task Hundred_point_exams_reject_scores_over_100(string examName)
    {
        await using var db = TestDb.Create();
        var student = CreateStudent("10000000146", "student@example.test");
        var exam = new Exam { ExamName = examName };
        db.AddRange(student, exam);
        await db.SaveChangesAsync();

        var result = await CreateService(db).CreateAsync(
            student.Tc,
            Request(exam.ExamId, 100.01m, "2026-01-01"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains("0 ile 100", result.Error, StringComparison.Ordinal);
        Assert.Empty(db.StudentExamScores);
    }

    [Fact]
    public async Task Other_defined_exam_can_use_extended_score_range()
    {
        await using var db = TestDb.Create();
        var student = CreateStudent("10000000146", "student@example.test");
        var exam = new Exam { ExamName = "GRE" };
        db.AddRange(student, exam);
        await db.SaveChangesAsync();

        var result = await CreateService(db).CreateAsync(
            student.Tc,
            Request(exam.ExamId, 340, "2026-01-01"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Future_date_and_duplicate_exam_are_rejected()
    {
        await using var db = TestDb.Create();
        var (student, _, ales, _) = await SeedAsync(db);
        var service = CreateService(db);

        var future = await service.CreateAsync(
            student.Tc,
            Request(ales.ExamId, 80, "2026-07-21"),
            CancellationToken.None);
        var first = await service.CreateAsync(
            student.Tc,
            Request(ales.ExamId, 80, "2026-07-19"),
            CancellationToken.None);
        var duplicate = await service.CreateAsync(
            student.Tc,
            Request(ales.ExamId, 85, "2026-07-18"),
            CancellationToken.None);

        Assert.False(future.IsSuccess);
        Assert.Contains("gelecekte olamaz", future.Error, StringComparison.Ordinal);
        Assert.True(first.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, duplicate.StatusCode);
        Assert.Single(db.StudentExamScores);
    }

    [Fact]
    public void Score_and_exam_date_are_required_by_api_contract()
    {
        var request = new StudentExamScoreInputDto { ExamId = 1 };
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        Assert.Contains(results, item => item.ErrorMessage == "Sınav puanı zorunludur.");
        Assert.Contains(results, item => item.ErrorMessage == "Sonuç tarihi zorunludur.");
    }

    private static StudentExamScoreService CreateService(GraduateAppDbContext db) =>
        new(db, new TestTimeProvider(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero)));

    private static StudentExamScoreInputDto Request(int examId, decimal score, string date) => new()
    {
        ExamId = examId,
        Score = score,
        ExamDate = DateOnly.Parse(date)
    };

    private static async Task<(Student Owner, Student Other, Exam Ales, Exam Yds)> SeedAsync(GraduateAppDbContext db)
    {
        var owner = CreateStudent("10000000146", "owner@example.test");
        var other = CreateStudent("10000000154", "other@example.test");
        var ales = new Exam { ExamName = "ALES" };
        var yds = new Exam { ExamName = "YDS" };
        db.AddRange(owner, other, ales, yds);
        await db.SaveChangesAsync();
        return (owner, other, ales, yds);
    }

    private static Student CreateStudent(string tc, string email) => new()
    {
        Tc = tc,
        StudentName = "Test",
        StudentSurname = "Öğrenci",
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        PasswordHash = "hash",
        SecurityStamp = Guid.NewGuid().ToString("N")
    };
}
