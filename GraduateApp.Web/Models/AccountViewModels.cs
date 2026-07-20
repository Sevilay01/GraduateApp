using System.ComponentModel.DataAnnotations;

namespace GraduateApp.Web.Models;

public enum LoginAccountType
{
    Student = 1,
    Admin = 2
}

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "TC kimlik numarası veya e-posta zorunludur.")]
    [StringLength(254, ErrorMessage = "Kullanıcı adı en fazla 254 karakter olabilir.")]
    [Display(Name = "TC kimlik numarası veya e-posta")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Parola zorunludur.")]
    [DataType(DataType.Password)]
    [StringLength(128, ErrorMessage = "Parola en fazla 128 karakter olabilir.")]
    [Display(Name = "Parola")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Bu cihazda oturumu açık tut")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }

    public LoginAccountType AccountType { get; set; } = LoginAccountType.Student;
    public bool HasExistingSession { get; set; }
    public string? ExistingRoleDisplay { get; set; }
}

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "TC kimlik numarası zorunludur.")]
    [RegularExpression("^[0-9]{11}$", ErrorMessage = "TC kimlik numarası 11 rakamdan oluşmalıdır.")]
    [Display(Name = "TC kimlik numarası")]
    public string Tc { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ad zorunludur.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Ad 2 ile 50 karakter arasında olmalıdır.")]
    [Display(Name = "Ad")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Soyad zorunludur.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Soyad 2 ile 50 karakter arasında olmalıdır.")]
    [Display(Name = "Soyad")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Baba adı zorunludur.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Baba adı 2 ile 50 karakter arasında olmalıdır.")]
    [Display(Name = "Baba adı")]
    public string FatherName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Doğum tarihi zorunludur.")]
    [DataType(DataType.Date)]
    [Display(Name = "Doğum tarihi")]
    public DateOnly? BirthDate { get; set; }

    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    [StringLength(254, ErrorMessage = "E-posta en fazla 254 karakter olabilir.")]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Telefon numarası zorunludur.")]
    [StringLength(30, ErrorMessage = "Telefon numarası en fazla 30 karakter olabilir.")]
    [DataType(DataType.PhoneNumber)]
    [Display(Name = "Cep telefonu")]
    public string Telephone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Parola zorunludur.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Parola 12 ile 128 karakter arasında olmalıdır.")]
    [DataType(DataType.Password)]
    [Display(Name = "Parola")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Parola tekrarı zorunludur.")]
    [Compare(nameof(Password), ErrorMessage = "Parolalar eşleşmiyor.")]
    [DataType(DataType.Password)]
    [Display(Name = "Parola tekrarı")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    [StringLength(254, ErrorMessage = "E-posta en fazla 254 karakter olabilir.")]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel
{
    [Required(ErrorMessage = "Parola sıfırlama bağlantısı zorunludur.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni parola zorunludur.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Yeni parola 12 ile 128 karakter arasında olmalıdır.")]
    [DataType(DataType.Password)]
    [Display(Name = "Yeni parola")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni parola tekrarı zorunludur.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Parolalar eşleşmiyor.")]
    [DataType(DataType.Password)]
    [Display(Name = "Yeni parola tekrarı")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Mevcut parola zorunludur.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mevcut parola")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni parola zorunludur.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Yeni parola 12 ile 128 karakter arasında olmalıdır.")]
    [DataType(DataType.Password)]
    [Display(Name = "Yeni parola")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni parola tekrarı zorunludur.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Parolalar eşleşmiyor.")]
    [DataType(DataType.Password)]
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
