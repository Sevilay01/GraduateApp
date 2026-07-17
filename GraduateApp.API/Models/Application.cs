namespace GraduateApp.API.Models;

public sealed class Application
{
    public int ApplicationId { get; set; }
    public string Tc { get; set; } = string.Empty;
    public int ProgramId { get; set; }
    public DateTime ApplicationDate { get; set; }
    public string CurrentStatus { get; set; } = Domain.ApplicationStatus.Pending.ToString();
    public byte[] RowVersion { get; set; } = [];

    public ICollection<ApplicationStatusHistory> ApplicationStatusHistories { get; set; } = new List<ApplicationStatusHistory>();
    public Program Program { get; set; } = null!;
    public ICollection<ReferenceLetter> ReferenceLetters { get; set; } = new List<ReferenceLetter>();
    public Student TcNavigation { get; set; } = null!;
}
