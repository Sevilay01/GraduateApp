using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class Institute
{
    public int InstituteId { get; set; }

    public string InstituteName { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public virtual ICollection<Program> Programs { get; set; } = new List<Program>();
}
