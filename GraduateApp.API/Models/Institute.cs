using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class Institute
{
    public int InstituteId { get; set; }

    public string InstituteName { get; set; } = null!;

    public virtual ICollection<Program> Programs { get; set; } = new List<Program>();
}
