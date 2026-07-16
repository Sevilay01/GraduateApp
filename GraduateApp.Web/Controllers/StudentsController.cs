using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using GraduateApp.Web.Models;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System;

namespace GraduateApp.Web.Controllers
{
    public class StudentsController : Controller
    {
        private readonly HttpClient _httpClient;

        public StudentsController()
        {
            _httpClient = new HttpClient();
            // Kendi API portunu (örn: 5158) buraya yazmalýsýn
            _httpClient.BaseAddress = new Uri("http://localhost:5158/");
        }

        public async Task<IActionResult> Index()
        {
            var students = new List<StudentViewModel>();

            HttpResponseMessage response = await _httpClient.GetAsync("api/students");

            if (response.IsSuccessStatusCode)
            {
                string data = await response.Content.ReadAsStringAsync();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                var apiVerisi = JsonSerializer.Deserialize<List<StudentViewModel>>(data, options);

                if (apiVerisi != null)
                {
                    students = apiVerisi;
                }
            }

            return View(students);
        }

        // 1. Boþ formu ekranda göstermek için (GET)
        [HttpGet]
        public IActionResult Create()
        {
            return View(); // Sadece boþ HTML sayfasýný ekrana basar
        }

        // 2. Formdaki "Kaydet" butonuna basýldýðýnda veriyi yakalayýp API'ye atmak için (POST)
        [HttpPost]
        public async Task<IActionResult> Create(StudentViewModel student)
        {
            // API þifre zorunlu dediði için arka planda varsayýlan bir þifre atýyoruz
            student.PasswordHash = "123456";

            // Modeldeki veriyi JSON formatýna çeviriyoruz
            var jsonContent = new StringContent(JsonSerializer.Serialize(student), System.Text.Encoding.UTF8, "application/json");

            // API'ye POST isteði atýyoruz
            HttpResponseMessage response = await _httpClient.PostAsync("api/students", jsonContent);

            if (response.IsSuccessStatusCode)
            {
                return RedirectToAction("Index");
            }

            string errorMsg = await response.Content.ReadAsStringAsync();
            ModelState.AddModelError(string.Empty, "API Hatasý: " + errorMsg);

            return View(student);
        }
    }
}