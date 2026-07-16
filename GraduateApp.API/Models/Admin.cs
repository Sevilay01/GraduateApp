using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class Admin
{
    public int AdminId { get; set; }

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public virtual ICollection<ApplicationStatusHistory> ApplicationStatusHistories { get; set; } = new List<ApplicationStatusHistory>();

    public virtual ICollection<SystemLog> SystemLogs { get; set; } = new List<SystemLog>();
}
