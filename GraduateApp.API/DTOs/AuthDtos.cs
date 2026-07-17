using System.ComponentModel.DataAnnotations;

namespace GraduateApp.API.DTOs;

public sealed class LoginDto
{
    [Required, StringLength(254)]
    public string Username { get; init; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; init; } = string.Empty;
}

public sealed class RegisterStudentDto
{
    [Required, RegularExpression("^[0-9]{11}$", ErrorMessage = "TC kimlik numarası 11 rakamdan oluşmalıdır.")]
    public string Tc { get; init; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    public string FirstName { get; init; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    public string LastName { get; init; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;

    [Required, Compare(nameof(Password))]
    public string ConfirmPassword { get; init; } = string.Empty;
}

public sealed class ForgotPasswordDto
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;
}

public sealed class ResetPasswordDto
{
    [Required, StringLength(2048)]
    public string Token { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string NewPassword { get; init; } = string.Empty;

    [Required, Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; init; } = string.Empty;
}

public sealed class ChangePasswordDto
{
    [Required, StringLength(128)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string NewPassword { get; init; } = string.Empty;

    [Required, Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; init; } = string.Empty;
}

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    string Role,
    string DisplayName,
    bool MustChangePassword);
