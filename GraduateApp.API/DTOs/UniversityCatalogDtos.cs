using System.ComponentModel.DataAnnotations;

namespace GraduateApp.API.DTOs;

public sealed class UniversityCreateDto
{
    [Required(ErrorMessage = "Üniversite adı zorunludur.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Üniversite adı 2 ile 100 karakter arasında olmalıdır.")]
    public string UniversityName { get; init; } = string.Empty;
}
