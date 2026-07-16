using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class Program
{
    public int ProgramId { get; set; }

    public int InstituteId { get; set; }

    public string ProgramName { get; set; } = null!;

    public string? DegreeType { get; set; }

    public virtual ICollection<Application> Applications { get; set; } = new List<Application>();

    public virtual Institute Institute { get; set; } = null!;
}
