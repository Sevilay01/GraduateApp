using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Web.Controllers;

[Authorize(Roles = "Student")]
public sealed class PanelController(GraduateApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var programsTask = apiClient.GetOpenProgramsAsync(cancellationToken);
        var applicationsTask = apiClient.GetMyApplicationsAsync(cancellationToken);
        await Task.WhenAll(programsTask, applicationsTask);
        var programs = await programsTask;
        var applications = await applicationsTask;

        return View(new PanelDashboardViewModel
        {
            OpenPrograms = programs.Value ?? [],
            Applications = applications.Value ?? [],
            ErrorMessage = programs.IsSuccess && applications.IsSuccess
                ? null
                : programs.Error ?? applications.Error ?? "Panel bilgileri yüklenemedi."
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(int programId, CancellationToken cancellationToken)
    {
        if (programId <= 0)
        {
            TempData["ErrorMessage"] = "Geçerli bir program seçiniz.";
            return RedirectToAction(nameof(Index));
        }

        var result = await apiClient.CreateApplicationAsync(programId, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Başvurunuz başarıyla alındı."
            : result.Error ?? "Başvuru oluşturulamadı.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        var profileTask = apiClient.GetProfileAsync(cancellationToken);
        var universitiesTask = apiClient.GetUniversitiesAsync(cancellationToken);
        await Task.WhenAll(profileTask, universitiesTask);
        var profile = await profileTask;
        var universities = await universitiesTask;

        if (!profile.IsSuccess || profile.Value is null)
        {
            TempData["ErrorMessage"] = profile.Error ?? "Profil bilgileri yüklenemedi.";
            return RedirectToAction(nameof(Index));
        }

        return View(MapProfile(profile.Value, universities.Value ?? []));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(StudentProfileViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.Universities = (await apiClient.GetUniversitiesAsync(cancellationToken)).Value ?? [];
            return View(model);
        }

        var result = await apiClient.UpdateProfileAsync(model, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Profil güncellenemedi.");
            model.Universities = (await apiClient.GetUniversitiesAsync(cancellationToken)).Value ?? [];
            return View(model);
        }

        TempData["SuccessMessage"] = "Profil ve eğitim bilgileriniz güncellendi.";
        return RedirectToAction(nameof(Profile));
    }

    private static StudentProfileViewModel MapProfile(
        StudentProfileApiModel profile,
        IReadOnlyList<UniversityViewModel> universities) => new()
        {
            TcMasked = profile.TcMasked,
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Email = profile.Email,
            Telephone = profile.Telephone,
            FatherName = profile.FatherName,
            BirthDate = profile.BirthDate,
            UniversityId = profile.Education?.UniversityId,
            Faculty = profile.Education?.Faculty,
            GraduatedProgram = profile.Education?.GraduatedProgram,
            Gno = profile.Education?.Gno,
            Universities = universities
        };
}
