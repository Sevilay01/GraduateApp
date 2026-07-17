using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Services;

public interface IStudentProfileService
{
    Task<StudentProfileDto?> GetAsync(string studentTc, CancellationToken cancellationToken);
    Task<ServiceResult<StudentProfileDto>> UpdateAsync(string studentTc, UpdateStudentProfileDto request, CancellationToken cancellationToken);
    Task<IReadOnlyList<UniversityDto>> GetUniversitiesAsync(CancellationToken cancellationToken);
}

public sealed class StudentProfileService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider) : IStudentProfileService
{
    public async Task<StudentProfileDto?> GetAsync(string studentTc, CancellationToken cancellationToken)
    {
        var student = await dbContext.Students.AsNoTracking()
            .Include(item => item.EducationInfos)
            .SingleOrDefaultAsync(item => item.Tc == studentTc, cancellationToken);
        return student is null ? null : Map(student);
    }

    public async Task<ServiceResult<StudentProfileDto>> UpdateAsync(
        string studentTc,
        UpdateStudentProfileDto request,
        CancellationToken cancellationToken)
    {
        var student = await dbContext.Students
            .Include(item => item.EducationInfos)
            .SingleOrDefaultAsync(item => item.Tc == studentTc, cancellationToken);
        if (student is null)
        {
            return ServiceResult<StudentProfileDto>.Failure("Öğrenci profili bulunamadı.", StatusCodes.Status404NotFound);
        }

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        if (await dbContext.Students.AnyAsync(
            item => item.Tc != studentTc
                && (item.NormalizedEmail == normalizedEmail || item.Email.ToUpper() == normalizedEmail),
            cancellationToken))
        {
            return ServiceResult<StudentProfileDto>.Failure("E-posta adresi başka bir hesap tarafından kullanılıyor.", StatusCodes.Status409Conflict);
        }

        if (!string.IsNullOrWhiteSpace(request.Telephone)
            && await dbContext.Students.AnyAsync(
                item => item.Tc != studentTc && item.Telephone == request.Telephone.Trim(),
                cancellationToken))
        {
            return ServiceResult<StudentProfileDto>.Failure("Telefon numarası başka bir hesap tarafından kullanılıyor.", StatusCodes.Status409Conflict);
        }

        student.StudentName = request.FirstName.Trim();
        student.StudentSurname = request.LastName.Trim();
        student.Email = request.Email.Trim();
        student.NormalizedEmail = normalizedEmail;
        student.Telephone = string.IsNullOrWhiteSpace(request.Telephone) ? null : request.Telephone.Trim();
        student.FatherName = string.IsNullOrWhiteSpace(request.FatherName) ? null : request.FatherName.Trim();
        student.BirthDate = request.BirthDate;
        student.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (request.Education is not null)
        {
            if (!await dbContext.Universities.AnyAsync(
                item => item.UniversityId == request.Education.UniversityId,
                cancellationToken))
            {
                return ServiceResult<StudentProfileDto>.Failure("Üniversite bulunamadı.", StatusCodes.Status400BadRequest);
            }

            var education = student.EducationInfos.OrderBy(item => item.EducationId).FirstOrDefault();
            if (education is null)
            {
                education = new EducationInfo { Tc = studentTc };
                student.EducationInfos.Add(education);
            }

            education.UniversityId = request.Education.UniversityId;
            education.Faculty = NormalizeOptional(request.Education.Faculty);
            education.GraduatedProgram = NormalizeOptional(request.Education.GraduatedProgram);
            education.Gno = request.Education.Gno;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult<StudentProfileDto>.Success(Map(student));
        }
        catch (DbUpdateException)
        {
            return ServiceResult<StudentProfileDto>.Failure("Profil bilgileri güncellenemedi.", StatusCodes.Status409Conflict);
        }
    }

    public async Task<IReadOnlyList<UniversityDto>> GetUniversitiesAsync(CancellationToken cancellationToken) =>
        await dbContext.Universities.AsNoTracking()
            .OrderBy(item => item.UniversityName)
            .Select(item => new UniversityDto(item.UniversityId, item.UniversityName))
            .ToListAsync(cancellationToken);

    private static StudentProfileDto Map(Student student)
    {
        var education = student.EducationInfos.OrderBy(item => item.EducationId).FirstOrDefault();
        return new StudentProfileDto(
            student.Tc.Length >= 4 ? $"*******{student.Tc[^4..]}" : "***********",
            student.StudentName,
            student.StudentSurname,
            student.Email,
            student.Telephone,
            student.FatherName,
            student.BirthDate,
            education is null
                ? null
                : new EducationDto
                {
                    UniversityId = education.UniversityId,
                    Faculty = education.Faculty,
                    GraduatedProgram = education.GraduatedProgram,
                    Gno = education.Gno
                });
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
