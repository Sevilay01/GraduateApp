using System.ComponentModel.DataAnnotations;

namespace GraduateApp.API.DTOs;

public enum AdminAccountStatus
{
    Active = 1,
    Inactive = 2,
    InvitationPending = 3,
    Locked = 4
}

public sealed record AdminAccountDto(
    Guid PublicId,
    string Email,
    bool IsActive,
    bool IsInvitationPending,
    bool IsLocked,
    int AccessFailedCount,
    bool IsCurrentAdmin,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string RowVersion);

public sealed class InviteAdminDto
{
    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    [StringLength(254, ErrorMessage = "E-posta en fazla 254 karakter olabilir.")]
    public string Email { get; init; } = string.Empty;
}

public sealed class AdminAccountConcurrencyDto
{
    [Required(ErrorMessage = "Yönetici eşzamanlılık bilgisi zorunludur.")]
    [StringLength(24, MinimumLength = 12, ErrorMessage = "Yönetici eşzamanlılık bilgisi geçersizdir.")]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class AcceptAdminInvitationDto
{
    [Required(ErrorMessage = "Davet bağlantısı zorunludur.")]
    [StringLength(2048, ErrorMessage = "Davet bağlantısı geçersizdir.")]
    public string Token { get; init; } = string.Empty;

    [Required(ErrorMessage = "Yeni parola zorunludur.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Yeni parola 12 ile 128 karakter arasında olmalıdır.")]
    public string NewPassword { get; init; } = string.Empty;

    [Required(ErrorMessage = "Yeni parola tekrarı zorunludur.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Parolalar eşleşmiyor.")]
    public string ConfirmPassword { get; init; } = string.Empty;
}
