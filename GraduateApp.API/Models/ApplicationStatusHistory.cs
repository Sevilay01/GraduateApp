using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class ApplicationStatusHistory
{
    public int HistoryId { get; set; }

    public int ApplicationId { get; set; }

    public string StatusName { get; set; } = null!;

    public int? ChangedByAdminId { get; set; }

    public DateTime? ChangeDate { get; set; }

    public string? Notes { get; set; }

    public virtual Application Application { get; set; } = null!;

    public virtual Admin? ChangedByAdmin { get; set; }
}
