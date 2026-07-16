namespace GraduateApp.API.Models // Sende klasör adı farklıysa burayı kendi namespace'ine göre ayarla
{
    public class LoginDto
    {
        public string TC { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}