using System.ComponentModel.DataAnnotations;
using GraduateApp.API.Models;

namespace GraduateApp.API.DTOs;

public sealed class LoginDto
{
    [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
    [StringLength(254, ErrorMessage = "Kullanıcı adı en fazla 254 karakter olabilir.")]
    public string Username { get; init; } = string.Empty;

    [Required(ErrorMessage = "Parola zorunludur.")]
    [StringLength(128, ErrorMessage = "Parola en fazla 128 karakter olabilir.")]
    public string Password { get; init; } = string.Empty;

    [Required(ErrorMessage = "Hesap türü zorunludur.")]
    [EnumDataType(typeof(LoginAccountType), ErrorMessage = "Hesap türü geçersizdir.")]
    public LoginAccountType? AccountType { get; init; }
}

public sealed class RegisterStudentDto
{
    [Required(ErrorMessage = "TC kimlik numarası zorunludur.")]
    [RegularExpression("^[0-9]{11}$", ErrorMessage = "TC kimlik numarası 11 rakamdan oluşmalıdır.")]
    public string Tc { get; init; } = string.Empty;

    [Required(ErrorMessage = "Ad zorunludur.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Ad 2 ile 50 karakter arasında olmalıdır.")]
    public string FirstName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Soyad zorunludur.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Soyad 2 ile 50 karakter arasında olmalıdır.")]
    public string LastName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Baba adı zorunludur.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Baba adı 2 ile 50 karakter arasında olmalıdır.")]
    public string FatherName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Doğum tarihi zorunludur.")]
    public DateOnly BirthDate { get; init; }

    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    [StringLength(254, ErrorMessage = "E-posta en fazla 254 karakter olabilir.")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Telefon numarası zorunludur.")]
    [StringLength(30, ErrorMessage = "Telefon numarası en fazla 30 karakter olabilir.")]
    public string Telephone { get; init; } = string.Empty;

    [Required(ErrorMessage = "Parola zorunludur.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Parola 12 ile 128 karakter arasında olmalıdır.")]
    public string Password { get; init; } = string.Empty;

    [Required(ErrorMessage = "Parola tekrarı zorunludur.")]
    [Compare(nameof(Password), ErrorMessage = "Parolalar eşleşmiyor.")]
    public string ConfirmPassword { get; init; } = string.Empty;
}

public sealed class ForgotPasswordDto
{
    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    [StringLength(254, ErrorMessage = "E-posta en fazla 254 karakter olabilir.")]
    public string Email { get; init; } = string.Empty;
}

public sealed class ResetPasswordDto
{
    [Required(ErrorMessage = "Parola sıfırlama bağlantısı zorunludur.")]
    [StringLength(2048, ErrorMessage = "Parola sıfırlama bağlantısı geçersizdir.")]
    public string Token { get; init; } = string.Empty;

    [Required(ErrorMessage = "Yeni parola zorunludur.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Yeni parola 12 ile 128 karakter arasında olmalıdır.")]
    public string NewPassword { get; init; } = string.Empty;

    [Required(ErrorMessage = "Yeni parola tekrarı zorunludur.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Parolalar eşleşmiyor.")]
    public string ConfirmPassword { get; init; } = string.Empty;
}

public sealed class ChangePasswordDto
{
    [Required(ErrorMessage = "Mevcut parola zorunludur.")]
    [StringLength(128, ErrorMessage = "Mevcut parola en fazla 128 karakter olabilir.")]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required(ErrorMessage = "Yeni parola zorunludur.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Yeni parola 12 ile 128 karakter arasında olmalıdır.")]
    public string NewPassword { get; init; } = string.Empty;

    [Required(ErrorMessage = "Yeni parola tekrarı zorunludur.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Parolalar eşleşmiyor.")]
    public string ConfirmPassword { get; init; } = string.Empty;
}

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    string Role,
    string DisplayName,
    bool MustChangePassword);
