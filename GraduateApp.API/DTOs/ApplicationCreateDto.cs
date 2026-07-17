using System.ComponentModel.DataAnnotations;

namespace GraduateApp.API.DTOs;

public sealed class ApplicationCreateDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Geçerli bir dönemsel ilan seçiniz.")]
    public int ProgramOfferingId { get; init; }
}
