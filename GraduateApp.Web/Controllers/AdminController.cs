using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text;
using System;

// BU İKİ KÜTÜPHANEYİ EN ÜSTE EKLİYORUZ
using System.Collections.Generic;
using GraduateApp.Web.Models;

namespace GraduateApp.Web.Controllers
{
    public class AdminController : Controller
    {
        private readonly HttpClient _httpClient;

        public AdminController()
        {
            _httpClient = new HttpClient();
            // Kendi API portunun doğru olduğuna emin ol
            _httpClient.BaseAddress = new Uri("http://localhost:5158/");
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // API'den tüm başvuruları çek
            var res = await _httpClient.GetAsync("api/applications");
            var data = await res.Content.ReadAsStringAsync();

            // KÜTÜPHANELERİ ÜSTE EKLEDİĞİMİZ İÇİN BURASI ARTIK TERTEMİZ:
            var applications = JsonSerializer.Deserialize<List<AdminApplicationViewModel>>(data, options)
                               ?? new List<AdminApplicationViewModel>();

            return View(applications);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int id, string status)
        {
            var updateData = new { newStatus = status };
            var jsonContent = new StringContent(JsonSerializer.Serialize(updateData), Encoding.UTF8, "application/json");

            // API'ye yeni durumu gönder (PUT)
            var res = await _httpClient.PutAsync($"api/applications/{id}/status", jsonContent);

            if (res.IsSuccessStatusCode)
                TempData["SuccessMessage"] = "Başvuru durumu başarıyla güncellendi.";
            else
                TempData["ErrorMessage"] = "Durum güncellenirken bir hata oluştu.";

            return RedirectToAction("Index");
        }
    }
}