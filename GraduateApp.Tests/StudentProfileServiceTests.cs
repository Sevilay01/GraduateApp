using GraduateApp.API.Domain;
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

    [Fact]
    public async Task Update_with_unknown_university_keeps_the_existing_safe_bad_request_behavior()
    {
        await using var db = TestDb.Create();
        var student = CreateStudent("10000000078", "student@example.test", null);
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var service = new StudentProfileService(db, TimeProvider.System, new InvariantEmailNormalizer());

        var result = await service.UpdateAsync(student.Tc, new UpdateStudentProfileDto
        {
            FirstName = student.StudentName,
            LastName = student.StudentSurname,
            Email = student.Email,
            Education = new EducationDto
            {
                UniversityId = int.MaxValue,
                Faculty = "Test Fakültesi",
                GraduatedProgram = "Test Programı",
                Gno = 3.5m
            }
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("Üniversite bulunamadı.", result.Error);
        Assert.Empty(student.EducationInfos);
    }

    [Fact]
    public async Task Profile_gno_update_preserves_published_application_snapshot_score_rank_and_outcome()
    {
        await using var db = TestDb.Create();
        var student = CreateStudent("10000000078", "student@example.test", null);
        var university = new University { UniversityName = "Test Üniversitesi" };
        student.EducationInfos.Add(new EducationInfo
        {
            University = university,
            Faculty = "Mühendislik",
            GraduatedProgram = "Bilgisayar Mühendisliği",
            Gno = 3.25m
        });
        var offering = new ProgramOffering
        {
            Program = new GraduateApp.API.Models.Program
            {
                ProgramName = "Test Programı",
                DegreeType = "Tezli Yüksek Lisans",
                IsActive = true,
                Institute = new Institute { InstituteName = "Test Enstitüsü", IsActive = true }
            },
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall,
            Quota = 1,
            UsesEvaluationWorkflow = true,
            EvaluationState = OfferingEvaluationState.Published,
            ResultsPublishedAtUtc = new DateTime(2026, 8, 4, 9, 0, 0, DateTimeKind.Utc)
        };
        var evaluation = new ApplicationEvaluation
        {
            ProgramOffering = offering,
            EligibilityStatus = EvaluationEligibilityStatus.Eligible,
            TotalScore = 81.25m,
            Rank = 1,
            Outcome = EvaluationOutcome.Admitted,
            FinalizedAtUtc = new DateTime(2026, 8, 3, 9, 0, 0, DateTimeKind.Utc),
            Components =
            [
                new ApplicationEvaluationComponent
                {
                    CriterionPublicIdSnapshot = Guid.NewGuid(),
                    CodeSnapshot = "GPA",
                    DisplayNameSnapshot = "Lisans GNO",
                    SourceTypeSnapshot = EvaluationCriterionSourceType.UndergraduateGpa,
                    RawScore = 3.25m,
                    MaximumRawScoreSnapshot = 4m,
                    NormalizedScore = 81.25m,
                    WeightBasisPointsSnapshot = 10000,
                    WeightedScore = 81.25m,
                    TieBreakPrioritySnapshot = 1
                }
            ]
        };
        offering.Applications.Add(new Application
        {
            TcNavigation = student,
            ProgramOffering = offering,
            ApplicationDate = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc),
            CurrentStatus = ApplicationStatus.Approved.ToString(),
            UsesEvaluationWorkflow = true,
            Evaluation = evaluation
        });
        db.AddRange(student, offering);
        await db.SaveChangesAsync();
        var service = new StudentProfileService(db, TimeProvider.System, new InvariantEmailNormalizer());
        var gpaComponent = Assert.Single(evaluation.Components);

        var result = await service.UpdateAsync(student.Tc, new UpdateStudentProfileDto
        {
            FirstName = student.StudentName,
            LastName = student.StudentSurname,
            Email = student.Email,
            Education = new EducationDto
            {
                UniversityId = university.UniversityId,
                Faculty = "Mühendislik",
                GraduatedProgram = "Bilgisayar Mühendisliği",
                Gno = 3.75m
            }
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3.75m, Assert.Single(student.EducationInfos).Gno);
        Assert.Equal(3.25m, gpaComponent.RawScore);
        Assert.Equal(81.25m, gpaComponent.NormalizedScore);
        Assert.Equal(81.25m, gpaComponent.WeightedScore);
        Assert.Equal(81.25m, evaluation.TotalScore);
        Assert.Equal(1, evaluation.Rank);
        Assert.Equal(EvaluationOutcome.Admitted, evaluation.Outcome);
        Assert.Equal(OfferingEvaluationState.Published, offering.EvaluationState);
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
