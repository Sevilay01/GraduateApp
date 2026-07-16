using System;

namespace GraduateApp.Web.Models
{
    public class ProgramViewModel
    {
        public int ProgramId { get; set; }
        public string ProgramName { get; set; } = string.Empty;
        public string DegreeType { get; set; } = string.Empty;
    }

    public class PanelApplicationViewModel
    {
        public int ApplicationId { get; set; }
        public int ProgramId { get; set; }
        public string ProgramName { get; set; } = string.Empty;
        public DateTime? ApplicationDate { get; set; }
        public string CurrentStatus { get; set; } = string.Empty;
    }
}