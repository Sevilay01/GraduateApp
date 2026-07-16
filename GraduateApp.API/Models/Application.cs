using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class Application
{
    public int ApplicationId { get; set; }

    public string Tc { get; set; } = null!;

    public int ProgramId { get; set; }

    public DateTime? ApplicationDate { get; set; }

    public string? CurrentStatus { get; set; }

    public virtual ICollection<ApplicationStatusHistory> ApplicationStatusHistories { get; set; } = new List<ApplicationStatusHistory>();

    public virtual Program Program { get; set; } = null!;

    public virtual ICollection<ReferenceLetter> ReferenceLetters { get; set; } = new List<ReferenceLetter>();

    public virtual Student TcNavigation { get; set; } = null!;
}
