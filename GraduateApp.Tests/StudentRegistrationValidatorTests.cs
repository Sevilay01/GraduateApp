using GraduateApp.API.DTOs;
using GraduateApp.API.Services;
using Microsoft.Extensions.Options;

namespace GraduateApp.Tests;

public sealed class StudentRegistrationValidatorTests
{
    [Theory]
    [InlineData("5321234567")]
    [InlineData("05321234567")]
    [InlineData("+905321234567")]
    [InlineData("00905321234567")]
    [InlineData("0 (532) 123-45-67")]
    public void Validate_normalizes_supported_turkish_mobile_formats(string input)
    {
        var result = CreateValidator().Validate(CreateRequest(input));

        Assert.True(result.IsValid);
        Assert.Equal("+905321234567", result.Value!.Telephone);
    }

    [Theory]
    [InlineData("02121234567")]
    [InlineData("+90532123456")]
    [InlineData("+90532123456A")]
    [InlineData("+90 532 123 45 67 ext 1")]
    public void Validate_rejects_invalid_or_non_mobile_telephone(string input)
    {
        var result = CreateValidator().Validate(CreateRequest(input));

        Assert.False(result.IsValid);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Validate_trims_names_and_preserves_unicode()
    {
        var request = CreateRequest(
            "05321234567",
            firstName: "  İrem  ",
            lastName: "  Öztürk  ",
            fatherName: "  Ali Rıza  ");

        var result = CreateValidator().Validate(request);

        Assert.True(result.IsValid);
        Assert.Equal("İrem", result.Value!.FirstName);
        Assert.Equal("Öztürk", result.Value.LastName);
        Assert.Equal("Ali Rıza", result.Value.FatherName);
    }

    [Fact]
    public void Validate_rejects_control_characters_in_names()
    {
        var result = CreateValidator().Validate(CreateRequest("05321234567", fatherName: "Ali\u0007"));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("2026-07-19")]
    [InlineData("2008-07-19")]
    [InlineData("1900-01-01")]
    public void Validate_rejects_future_underage_and_unreasonably_old_birth_dates(string birthDate)
    {
        var result = CreateValidator().Validate(CreateRequest(
            "05321234567",
            birthDate: DateOnly.Parse(birthDate)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_uses_istanbul_calendar_date_without_timezone_day_shift()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 7, 17, 21, 30, 0, TimeSpan.Zero));
        var result = CreateValidator(clock).Validate(CreateRequest(
            "05321234567",
            birthDate: new DateOnly(2008, 7, 18)));

        Assert.True(result.IsValid);
        Assert.Equal(new DateOnly(2008, 7, 18), result.Value!.BirthDate);
    }

    private static StudentRegistrationValidator CreateValidator(TestTimeProvider? clock = null) =>
        new(
            clock ?? new TestTimeProvider(new DateTimeOffset(2026, 7, 18, 9, 0, 0, TimeSpan.Zero)),
            Options.Create(new RegistrationOptions()));

    private static RegisterStudentDto CreateRequest(
        string telephone,
        string firstName = "Test",
        string lastName = "Öğrenci",
        string fatherName = "Test Baba",
        DateOnly? birthDate = null) => new()
        {
            Tc = "10000000078",
            FirstName = firstName,
            LastName = lastName,
            FatherName = fatherName,
            BirthDate = birthDate ?? new DateOnly(2000, 1, 1),
            Email = "student@example.test",
            Telephone = telephone,
            Password = "Valid-Password-1!",
            ConfirmPassword = "Valid-Password-1!"
        };
}
