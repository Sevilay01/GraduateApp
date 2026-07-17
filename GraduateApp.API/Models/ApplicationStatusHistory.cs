namespace GraduateApp.API.Models;

public sealed class ApplicationStatusHistory
{
    public int HistoryId { get; set; }
    public int ApplicationId { get; set; }
    public string? PreviousStatus { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int? ChangedByAdminId { get; set; }
    public DateTime ChangeDate { get; set; }
    public string? Notes { get; set; }

    public Application Application { get; set; } = null!;
    public Admin? ChangedByAdmin { get; set; }
}
