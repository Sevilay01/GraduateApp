namespace GraduateApp.Web.Models
{
    public class RegisterViewModel
    {
        public string TC { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string StudentSurname { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty; // Adayın kendi belirleyeceği şifre
    }
}