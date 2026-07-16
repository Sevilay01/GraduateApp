using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class ReferenceLetter
{
    public int ReferenceId { get; set; }

    public int ApplicationId { get; set; }

    public string AcademicianName { get; set; } = null!;

    public string AcademicianEmail { get; set; } = null!;

    public string FilePath { get; set; } = null!;

    public virtual Application Application { get; set; } = null!;
}
