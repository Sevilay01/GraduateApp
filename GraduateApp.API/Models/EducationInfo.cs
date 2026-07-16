using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class EducationInfo
{
    public int EducationId { get; set; }

    public string Tc { get; set; } = null!;

    public int UniversityId { get; set; }

    public string? Faculty { get; set; }

    public string? GraduatedProgram { get; set; }

    public decimal? Gno { get; set; }

    public string? DiplomaPath { get; set; }

    public string? TranscriptPath { get; set; }

    public virtual Student TcNavigation { get; set; } = null!;

    public virtual University University { get; set; } = null!;
}
