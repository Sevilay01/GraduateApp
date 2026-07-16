using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System;
using GraduateApp.Web.Models; // Modelimizin yerini derleyiciye kesin olarak gösteriyoruz

namespace GraduateApp.Web.Controllers
{
    public class ApplicationController : Controller
    {
        private readonly HttpClient _httpClient;

        public ApplicationController()
        {
            _httpClient = new HttpClient();
            //  API portunu (örn: 5158) buraya yazmalý
            _httpClient.BaseAddress = new Uri("http://localhost:5066/");
        }

        public async Task<IActionResult> Index()
        {
            // Derleyicinin kafasý karýþmasýn diye 'var' ile tanýmlýyoruz
            var applications = new List<ApplicationViewModel>();

            HttpResponseMessage response = await _httpClient.GetAsync("api/applications");

            if (response.IsSuccessStatusCode)
            {
                string data = await response.Content.ReadAsStringAsync();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                // JsonSerializer iþlemini büyüktür/küçüktür sanmamasý için tam tanýmlama yapýyoruz
                var apiVerisi = JsonSerializer.Deserialize<List<ApplicationViewModel>>(data, options);

                if (apiVerisi != null)
                {
                    applications = apiVerisi;
                }
            }

            return View(applications);
        }
        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int applicationId, string newStatus)
        {
            if (string.IsNullOrEmpty(newStatus))
            {
                return RedirectToAction("Index"); // Durum seçilmediyse bir þey yapma
            }

            // API'ye yeni durumu URL üzerinden yolluyoruz
            string url = $"api/applications/{applicationId}/status?newStatus={newStatus}";

            HttpResponseMessage response = await _httpClient.PostAsync(url, null);

            // Ýþlem bittikten sonra tabloyu (Index) yeniden yükle
            return RedirectToAction("Index");
        }
    }
}