using System.ComponentModel.DataAnnotations;
using GraduateApp.API.Domain;

namespace GraduateApp.API.DTOs;

public sealed class ApplicationStatusUpdateDto
{
    [EnumDataType(typeof(ApplicationStatus), ErrorMessage = "Geçersiz başvuru durumu.")]
    public ApplicationStatus NewStatus { get; init; }

    [Required]
    public string RowVersion { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Notes { get; init; }
}
