namespace GraduateApp.Web.Models
{
    public class StudentViewModel
    {
        public string TC { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string StudentSurname { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
    }
}