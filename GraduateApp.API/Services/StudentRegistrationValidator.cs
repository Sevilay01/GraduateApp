using GraduateApp.API.DTOs;
using Microsoft.Extensions.Options;

namespace GraduateApp.API.Services;

public sealed record NormalizedStudentRegistration(
    string FirstName,
    string LastName,
    string FatherName,
    DateOnly BirthDate,
    string Email,
    string Telephone);

public sealed record StudentRegistrationValidationResult(
    bool IsValid,
    NormalizedStudentRegistration? Value,
    string? Error)
{
    public static StudentRegistrationValidationResult Success(NormalizedStudentRegistration value) =>
        new(true, value, null);

    public static StudentRegistrationValidationResult Failure(string error) =>
        new(false, null, error);
}

public sealed class StudentRegistrationValidator(
    TimeProvider timeProvider,
    IOptions<RegistrationOptions> options)
{
    private readonly RegistrationOptions registrationOptions = options.Value;

    public StudentRegistrationValidationResult Validate(RegisterStudentDto request)
    {
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();
        var fatherName = request.FatherName.Trim();
        var email = request.Email.Trim();

        if (ContainsControlCharacter(firstName)
            || ContainsControlCharacter(lastName)
            || ContainsControlCharacter(fatherName))
        {
            return StudentRegistrationValidationResult.Failure("Ad alanları geçersiz karakter içeriyor.");
        }

        if (!TurkishMobilePhoneNormalizer.TryNormalize(request.Telephone, out var telephone))
        {
            return StudentRegistrationValidationResult.Failure("Geçerli bir Türkiye cep telefonu numarası giriniz.");
        }

        var today = GetCurrentLocalDate();
        if (request.BirthDate > today)
        {
            return StudentRegistrationValidationResult.Failure("Doğum tarihi gelecekte olamaz.");
        }

        var age = CalculateAge(request.BirthDate, today);
        if (age < registrationOptions.MinimumAge)
        {
            return StudentRegistrationValidationResult.Failure(
                $"Kayıt için en az {registrationOptions.MinimumAge} yaşında olmalısınız.");
        }

        if (age > registrationOptions.MaximumAge)
        {
            return StudentRegistrationValidationResult.Failure("Doğum tarihi makul aralığın dışında.");
        }

        return StudentRegistrationValidationResult.Success(new NormalizedStudentRegistration(
            firstName,
            lastName,
            fatherName,
            request.BirthDate,
            email,
            telephone));
    }

    private DateOnly GetCurrentLocalDate()
    {
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(registrationOptions.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
        }

        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private static int CalculateAge(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age))
        {
            age--;
        }

        return age;
    }

    private static bool ContainsControlCharacter(string value) => value.Any(char.IsControl);
}

public static class TurkishMobilePhoneNormalizer
{
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input) || input.Any(char.IsControl))
        {
            return false;
        }

        var compact = new string(input.Trim()
            .Where(character => character is not (' ' or '(' or ')' or '-'))
            .ToArray());
        string nationalNumber;
        if (compact.StartsWith("+90", StringComparison.Ordinal))
        {
            nationalNumber = compact[3..];
        }
        else if (compact.StartsWith("0090", StringComparison.Ordinal))
        {
            nationalNumber = compact[4..];
        }
        else if (compact.StartsWith('0'))
        {
            nationalNumber = compact[1..];
        }
        else
        {
            nationalNumber = compact;
        }

        if (nationalNumber.Length != 10
            || nationalNumber[0] != '5'
            || nationalNumber.Any(character => !char.IsAsciiDigit(character)))
        {
            return false;
        }

        normalized = $"+90{nationalNumber}";
        return true;
    }
}
