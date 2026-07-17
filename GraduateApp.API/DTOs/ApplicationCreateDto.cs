using System.ComponentModel.DataAnnotations;

namespace GraduateApp.API.DTOs;

public sealed class ApplicationCreateDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir program seçiniz.")]
    public int ProgramId { get; init; }
}
