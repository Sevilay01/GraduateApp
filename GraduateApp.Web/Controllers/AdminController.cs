using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Web.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminController(GraduateApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        ApplicationStatus? status,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await apiClient.GetAdminApplicationsAsync(search, status, page, pageSize, cancellationToken);
        return View(new AdminApplicationListViewModel
        {
            Result = result.Value ?? new PagedResultViewModel<AdminApplicationListItemViewModel>
            {
                Page = Math.Max(page, 1),
                PageSize = Math.Clamp(pageSize, 10, 100)
            },
            Search = search,
            Status = status,
            ErrorMessage = result.IsSuccess ? null : result.Error
        });
    }

    [HttpGet]
    public async Task<IActionResult> Detail(int id, CancellationToken cancellationToken)
    {
        var result = await apiClient.GetAdminApplicationDetailAsync(id, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            TempData["ErrorMessage"] = result.Error ?? "Başvuru bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        return View(result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(UpdateApplicationStatusViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Durum güncelleme bilgileri geçersiz.";
            return RedirectToAction(nameof(Detail), new { id = model.ApplicationId });
        }

        var result = await apiClient.UpdateApplicationStatusAsync(model, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Başvuru durumu güncellendi."
            : result.Error ?? "Başvuru durumu güncellenemedi.";
        return RedirectToAction(nameof(Detail), new { id = model.ApplicationId });
    }
}
