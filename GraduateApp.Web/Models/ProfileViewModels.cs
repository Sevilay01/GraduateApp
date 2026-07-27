using System.ComponentModel.DataAnnotations;
using GraduateApp.Web.Validation;

namespace GraduateApp.Web.Models;

public sealed class StudentProfileViewModel
{
    public string TcMasked { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    [Display(Name = "Ad")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    [Display(Name = "Soyad")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;

    [Phone, StringLength(30)]
    [Display(Name = "Telefon")]
    public string? Telephone { get; set; }

    [StringLength(50)]
    [Display(Name = "Baba adı")]
    public string? FatherName { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Doğum tarihi")]
    public DateOnly? BirthDate { get; set; }

    [Display(Name = "Üniversite")]
    public int? UniversityId { get; set; }

    [StringLength(100)]
    [Display(Name = "Fakülte")]
    public string? Faculty { get; set; }

    [StringLength(100)]
    [Display(Name = "Mezun olunan program")]
    public string? GraduatedProgram { get; set; }

    [LocalizedDecimalRange(
        0,
        4,
        ErrorMessage = "{0} 0 ile 4 arasında geçerli bir ondalık sayı olmalıdır.")]
    [Display(Name = "Lisans not ortalaması")]
    public decimal? Gno { get; set; }

    public IReadOnlyList<UniversityViewModel> Universities { get; set; } = [];
    public bool UniversityCatalogLoadSucceeded { get; set; } = true;
    public string? UniversityCatalogErrorMessage { get; set; }
    public bool CanEditEducation =>
        UniversityCatalogLoadSucceeded && Universities.Count > 0;
}

public sealed class StudentProfileApiModel
{
    public string TcMasked { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telephone { get; set; }
    public string? FatherName { get; set; }
    public DateOnly? BirthDate { get; set; }
    public EducationApiModel? Education { get; set; }
}

public sealed class EducationApiModel
{
    public int UniversityId { get; set; }
    public string? Faculty { get; set; }
    public string? GraduatedProgram { get; set; }
    public decimal? Gno { get; set; }
}

public sealed class UniversityViewModel
{
    public int UniversityId { get; set; }
    public string UniversityName { get; set; } = string.Empty;
}
