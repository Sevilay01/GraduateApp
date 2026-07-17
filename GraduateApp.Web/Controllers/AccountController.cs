using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using GraduateApp.Web.Models;

namespace GraduateApp.Web.Controllers
{
    public class AccountController : Controller
    {
        // Sayfa ilk yüklendiğinde çalışacak (GET)
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // Form doldurulup Gönder butonuna basıldığında çalışacak (POST)
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            // Modeldeki Required kuralları sağlandı mı kontrolü
            if (!ModelState.IsValid)
                return View(model);

            // TODO: İleride bu bilgileri API'ye gönderip doğrulayacağız.
            // Şimdilik test edebilmen için statik (sabit) bir kontrol yapıyoruz.
            if (model.Username == "admin" && model.Password == "123456")
            {
                // Kimlik kartını oluşturuyoruz
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, model.Username),
                    new Claim(ClaimTypes.Role, "Admin")
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true // Kullanıcı tarayıcıyı kapatsa da hatırla
                };

                // Kullanıcıyı sisteme dahil ediyoruz (Çerezi oluşturuyoruz)
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                // Başarılı girişte Admin paneline yönlendir
                return RedirectToAction("Index", "Admin");
            }

            // Eğer şifre veya kullanıcı adı yanlışsa ekrana hata mesajı bas
            ModelState.AddModelError("", "Kullanıcı adı veya şifre hatalı.");
            return View(model);
        }

        // Çıkış yapma işlemi
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }
    }
}