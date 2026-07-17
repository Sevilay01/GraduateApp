using System;

namespace GraduateApp.Web.Models
{
    public class AdminApplicationViewModel
    {
        public int ApplicationId { get; set; }
        public string Tc { get; set; } = string.Empty;
        public string StudentFullName { get; set; } = string.Empty;
        public int ProgramId { get; set; }
        public string ProgramName { get; set; } = string.Empty;
        public DateTime? ApplicationDate { get; set; }
        public string CurrentStatus { get; set; } = string.Empty;
    }
}