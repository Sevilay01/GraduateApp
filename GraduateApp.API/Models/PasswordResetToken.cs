using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class PasswordResetToken
{
    public int TokenId { get; set; }

    public string Tc { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    public DateTime ExpirationDate { get; set; }

    public bool? IsUsed { get; set; }

    public virtual Student TcNavigation { get; set; } = null!;
}
