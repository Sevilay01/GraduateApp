using System;

namespace GraduateApp.Web.Models
{
    public class ApplicationViewModel
    {
        public int ApplicationID { get; set; }
        public string StudentFullName { get; set; } = string.Empty;
        public string ProgramName { get; set; } = string.Empty;
        public DateTime ApplicationDate { get; set; }
        public string CurrentStatus { get; set; } = string.Empty;
    }
}