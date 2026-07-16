using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Claims;
using System.Text;
using System;

namespace GraduateApp.Web.Controllers
{
    [Authorize]
    public class PanelController : Controller
    {
        private readonly HttpClient _httpClient;

        public PanelController()
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("http://localhost:5158/"); // KENDİ API PORTUNU YAZ
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            string tc = User.FindFirstValue("TC") ?? User.FindFirst("TC")?.Value ?? string.Empty;
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Hataları önlemek için List ve Model adlarını tam yolları (Full Path) ile belirtiyoruz
            var programsRes = await _httpClient.GetAsync("api/programs");
            var programsStr = await programsRes.Content.ReadAsStringAsync();
            var programs = JsonSerializer.Deserialize<System.Collections.Generic.List<GraduateApp.Web.Models.ProgramViewModel>>(programsStr, options)
                           ?? new System.Collections.Generic.List<GraduateApp.Web.Models.ProgramViewModel>();

            var appsRes = await _httpClient.GetAsync($"api/applications/student/{tc}");
            var appsStr = await appsRes.Content.ReadAsStringAsync();
            var applications = JsonSerializer.Deserialize<System.Collections.Generic.List<GraduateApp.Web.Models.PanelApplicationViewModel>>(appsStr, options)
                               ?? new System.Collections.Generic.List<GraduateApp.Web.Models.PanelApplicationViewModel>();

            ViewBag.Programs = programs;
            ViewBag.Applications = applications;

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Apply(int programId)
        {
            string tc = User.FindFirstValue("TC") ?? User.FindFirst("TC")?.Value ?? string.Empty;

            var applicationData = new { tc = tc, programId = programId };
            var jsonContent = new StringContent(JsonSerializer.Serialize(applicationData), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("api/applications", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                TempData["ErrorMessage"] = error;
            }
            else
            {
                TempData["SuccessMessage"] = "Başvurunuz başarıyla alındı!";
            }

            return RedirectToAction("Index");
        }
    }
}