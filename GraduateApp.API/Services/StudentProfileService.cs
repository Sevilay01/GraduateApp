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
    TimeProvider timeProvider,
    IEmailNormalizer emailNormalizer) : IStudentProfileService
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
            .Include(item => item.LoginIdentity)
            .SingleOrDefaultAsync(item => item.Tc == studentTc, cancellationToken);
        if (student is null)
        {
            return ServiceResult<StudentProfileDto>.Failure("Öğrenci profili bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (student.LoginIdentity is null
            || student.LoginIdentity.AccountType != LoginAccountType.Student
            || student.LoginIdentity.StudentTc != student.Tc)
        {
            return ServiceResult<StudentProfileDto>.Failure(
                "Hesap bilgileri doğrulanamadı. Lütfen destek birimiyle iletişime geçin.",
                StatusCodes.Status409Conflict);
        }

        if (!emailNormalizer.TryNormalize(request.Email, out var normalizedEmail))
        {
            return ServiceResult<StudentProfileDto>.Failure(
                "Geçerli bir e-posta adresi giriniz.",
                StatusCodes.Status400BadRequest);
        }
        if (await dbContext.LoginIdentities.AnyAsync(
            item => item.LoginIdentityId != student.LoginIdentity.LoginIdentityId
                && item.NormalizedEmail == normalizedEmail,
            cancellationToken))
        {
            return ServiceResult<StudentProfileDto>.Failure("E-posta adresi başka bir hesap tarafından kullanılıyor.", StatusCodes.Status409Conflict);
        }

        string? normalizedTelephone = null;
        if (!string.IsNullOrWhiteSpace(request.Telephone))
        {
            if (!TurkishMobilePhoneNormalizer.TryNormalize(request.Telephone, out normalizedTelephone))
            {
                return ServiceResult<StudentProfileDto>.Failure(
                    "Geçerli bir Türkiye cep telefonu numarası giriniz.",
                    StatusCodes.Status400BadRequest);
            }
        }

        if (normalizedTelephone is not null
            && await dbContext.Students.AnyAsync(
                item => item.Tc != studentTc && item.Telephone == normalizedTelephone,
                cancellationToken))
        {
            return ServiceResult<StudentProfileDto>.Failure("Telefon numarası başka bir hesap tarafından kullanılıyor.", StatusCodes.Status409Conflict);
        }

        student.StudentName = request.FirstName.Trim();
        student.StudentSurname = request.LastName.Trim();
        student.Email = request.Email.Trim();
        student.NormalizedEmail = normalizedEmail;
        student.LoginIdentity.NormalizedEmail = normalizedEmail;
        student.Telephone = normalizedTelephone;
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
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult<StudentProfileDto>.Failure("Profil bilgileri güncellenemedi.", StatusCodes.Status409Conflict);
        }
    }

    public async Task<IReadOnlyList<UniversityDto>> GetUniversitiesAsync(CancellationToken cancellationToken) =>
        await dbContext.Universities.AsNoTracking()
            .OrderBy(item => item.UniversityName)
            .ThenBy(item => item.UniversityId)
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
