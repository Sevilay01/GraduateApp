using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class SystemLog
{
    public int LogId { get; set; }

    public int? AdminId { get; set; }

    public string? Tc { get; set; }

    public string ActionType { get; set; } = null!;

    public string? LogDetails { get; set; }

    public DateTime? LogDate { get; set; }

    public virtual Admin? Admin { get; set; }

    public virtual Student? TcNavigation { get; set; }
}
