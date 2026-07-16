using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using GraduateApp.Web.Models;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System;

// O oturum ve kimlik doğrulama hatalarını çözen 3 kritik kütüphane:
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
            // Kendi API portunun (örn: 5158) doğru olduğuna emin ol
            _httpClient.BaseAddress = new Uri("http://localhost:5158/");
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
            var studentData = new
            {
                tc = model.TC,
                studentName = model.StudentName,
                studentSurname = model.StudentSurname,
                email = model.Email,
                passwordHash = model.Password
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(studentData), System.Text.Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _httpClient.PostAsync("api/students", jsonContent);

            if (response.IsSuccessStatusCode)
            {
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
            var jsonContent = new StringContent(JsonSerializer.Serialize(model), System.Text.Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _httpClient.PostAsync("api/students/login", jsonContent);

            if (response.IsSuccessStatusCode)
            {
                // 1. API'den dönen öğrenci bilgisini oku
                string data = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var studentData = JsonSerializer.Deserialize<StudentViewModel>(data, options);

                if (studentData != null)
                {
                    // 2. Çerez içine koyacağımız "Kimlik Kartı"nı (Claims) oluştur
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, studentData.TC), // Benzersiz kimlik
                        new Claim(ClaimTypes.Name, $"{studentData.StudentName} {studentData.StudentSurname}"), // İsim Soyisim
                        new Claim("TC", studentData.TC) // İleride başvuru yaparken bu TC'yi kullanacağız
                    };

                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                    // 3. Oturumu başlat!
                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(claimsIdentity),
                        new AuthenticationProperties { IsPersistent = true });

                    return RedirectToAction("Index", "Home");
                }
            }

            string errorMsg = await response.Content.ReadAsStringAsync();
            ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(errorMsg) ? "TC veya Şifre hatalı!" : errorMsg);

            return View(model);
        }

        // 5. Çıkış Yapma Metodu
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }
    }
}