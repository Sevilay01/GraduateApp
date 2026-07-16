using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using GraduateApp.Web.Models;
using System.Net.Http;
using System.Threading.Tasks;
using System;

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

        // 4. Aday giriş yap butonuna bastığında (POST)
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            // Arayüzdeki formu JSON formatına çevir
            var jsonContent = new StringContent(JsonSerializer.Serialize(model), System.Text.Encoding.UTF8, "application/json");

            // API'deki "login" uç noktasına veriyi gönder
            HttpResponseMessage response = await _httpClient.PostAsync("api/students/login", jsonContent);

            if (response.IsSuccessStatusCode)
            {
                // Şifre doğruysa giriş başarılı! 
                // İleride buraya Cookie/Session kodlarını ekleyeceğiz.
                return RedirectToAction("Index", "Home");
            }

            // API'den hata dönerse (TC veya şifre yanlışsa) ekranda göster
            string errorMsg = await response.Content.ReadAsStringAsync();
            ModelState.AddModelError(string.Empty, errorMsg);

            return View(model);
        }
    }
}