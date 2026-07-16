using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class VwAdminApplicationSummary
{
    public int BasvuruNo { get; set; }

    public string KimlikNo { get; set; } = null!;

    public string AdSoyad { get; set; } = null!;

    public string Eposta { get; set; } = null!;

    public string Enstitu { get; set; } = null!;

    public string Program { get; set; } = null!;

    public string? MezunOlduguUniversite { get; set; }

    public decimal? LisansOrtalamasi { get; set; }

    public DateTime? BasvuruTarihi { get; set; }

    public string? GuncelDurum { get; set; }
}
