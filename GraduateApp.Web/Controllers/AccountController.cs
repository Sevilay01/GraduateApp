using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using GraduateApp.Web.Models;
using System.Net.Http;
using System.Threading.Tasks;
using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
namespace GraduateApp.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly HttpClient _httpClient;

        public AccountController()
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("http://localhost:5158/"); // API Portun
        }

        // 1. Kayıt formunu ekranda göster (GET)
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        // 2. Aday formu doldurup gönderdiğinde (POST)
        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            // Adayın girdiği verileri API'nin beklediği formata (PasswordHash dahil) çeviriyoruz
            var studentData = new 
            {
                tc = model.TC,
                studentName = model.StudentName,
                studentSurname = model.StudentSurname,
                email = model.Email,
                passwordHash = model.Password // Adayın şifresini veritabanındaki şifre alanına eşliyoruz
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(studentData), System.Text.Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _httpClient.PostAsync("api/students", jsonContent);

            if (response.IsSuccessStatusCode)
            {
                // Kayıt başarılıysa şimdilik Ana Sayfaya gönderelim (Sonra Login sayfasına yönlendireceğiz)
                return RedirectToAction("Index", "Home");
            }

            string errorMsg = await response.Content.ReadAsStringAsync();
            ModelState.AddModelError(string.Empty, "Kayıt başarısız oldu: " + errorMsg);

            return View(model);
        }
        // 3. Giriş formunu ekranda göster (GET)
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task Login(LoginViewModel model)
        {
            var jsonContent = new StringContent(JsonSerializer.Serialize(model), System.Text.Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _httpClient.PostAsync("api/students/login", jsonContent);

            if (response.IsSuccessStatusCode)
            {
                // 1. API'den dönen öğrenci bilgisini oku
                string data = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var studentData = JsonSerializer.Deserialize(data, options);

                // 2. Çerez içine koyacağımız "Kimlik Kartı"nı (Claims) oluştur
                var claims = new List
        {
            new Claim(ClaimTypes.NameIdentifier, studentData.TC), // Benzersiz kimlik
            new Claim(ClaimTypes.Name, $"{studentData.StudentName} {studentData.StudentSurname}"), // İsim Soyisim
            new Claim("TC", studentData.TC) // İleride başvuru yaparken bu TC'yi kullanacağız
        };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                // 3. Oturumu başlat! Tarayıcıya çerezi yerleştiriyoruz.
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    new AuthenticationProperties { IsPersistent = true }); // Tarayıcı kapansa da hatırla

                return RedirectToAction("Index", "Home");
            }

            string errorMsg = await response.Content.ReadAsStringAsync();
            ModelState.AddModelError(string.Empty, errorMsg);

            return View(model);
        }

        // Çıkış Yapma Metodu
        public async Task Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }
    }
}