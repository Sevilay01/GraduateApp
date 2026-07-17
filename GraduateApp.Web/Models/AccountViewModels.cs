using System.ComponentModel.DataAnnotations;

namespace GraduateApp.Web.Models;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "TC kimlik numarası veya e-posta zorunludur.")]
    [StringLength(254)]
    [Display(Name = "TC kimlik numarası veya e-posta")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Parola zorunludur.")]
    [DataType(DataType.Password)]
    [StringLength(128)]
    [Display(Name = "Parola")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Bu cihazda oturumu açık tut")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Required, RegularExpression("^[0-9]{11}$", ErrorMessage = "TC kimlik numarası 11 rakamdan oluşmalıdır.")]
    [Display(Name = "TC kimlik numarası")]
    public string Tc { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    [Display(Name = "Ad")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    [Display(Name = "Soyad")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    [DataType(DataType.Password)]
    [Display(Name = "Parola")]
    public string Password { get; set; } = string.Empty;

    [Required, Compare(nameof(Password), ErrorMessage = "Parolalar eşleşmiyor.")]
    [DataType(DataType.Password)]
    [Display(Name = "Parola tekrarı")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ForgotPasswordViewModel
{
    [Required, EmailAddress, StringLength(254)]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    [DataType(DataType.Password)]
    [Display(Name = "Yeni parola")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, Compare(nameof(NewPassword), ErrorMessage = "Parolalar eşleşmiyor.")]
    [DataType(DataType.Password)]
    [Display(Name = "Yeni parola tekrarı")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password)]
    [Display(Name = "Mevcut parola")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)]
    [Display(Name = "Yeni parola")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, Compare(nameof(NewPassword), ErrorMessage = "Parolalar eşleşmiyor."), DataType(DataType.Password)]
    [Display(Name = "Yeni parola tekrarı")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class LoginApiResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string Role { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
}
