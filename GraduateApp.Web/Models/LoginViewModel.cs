using System.ComponentModel.DataAnnotations;

namespace GraduateApp.Web.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Kullanýcý adý veya e-posta zorunludur.")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Þifre zorunludur.")]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }
}