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
    public async Task<IActionResult> Apply(int programOfferingId, CancellationToken cancellationToken)
    {
        if (programOfferingId <= 0)
        {
            TempData["ErrorMessage"] = "Geçerli bir dönemsel ilan seçiniz.";
            return RedirectToAction(nameof(Index));
        }

        var result = await apiClient.CreateApplicationAsync(programOfferingId, cancellationToken);
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

    [HttpGet]
    public async Task<IActionResult> ExamScores(int? editId, CancellationToken cancellationToken)
    {
        var scoresTask = apiClient.GetMyExamScoresAsync(cancellationToken);
        var examsTask = apiClient.GetExamCatalogAsync(cancellationToken);
        await Task.WhenAll(scoresTask, examsTask);
        var scores = await scoresTask;
        var exams = await examsTask;
        var selected = editId.HasValue
            ? scores.Value?.SingleOrDefault(item => item.ScoreId == editId.Value)
            : null;

        return View(new StudentExamScoresPageViewModel
        {
            Scores = scores.Value ?? [],
            Exams = exams.Value ?? [],
            Form = selected is null
                ? new StudentExamScoreInputViewModel()
                : new StudentExamScoreInputViewModel
                {
                    ScoreId = selected.ScoreId,
                    ExamId = selected.ExamId,
                    Score = selected.Score,
                    ExamDate = selected.ExamDate
                },
            ErrorMessage = !scores.IsSuccess || !exams.IsSuccess
                ? scores.Error ?? exams.Error ?? "Sınav sonuçları yüklenemedi."
                : editId.HasValue && selected is null
                    ? "Düzenlemek istediğiniz sınav sonucu bulunamadı."
                    : null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveExamScore(
        [Bind(Prefix = "Form")] StudentExamScoreInputViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await RenderExamScoresAsync(model, null, cancellationToken);
        }

        var result = model.ScoreId > 0
            ? await apiClient.UpdateExamScoreAsync(model, cancellationToken)
            : await apiClient.CreateExamScoreAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Sınav sonucu kaydedilemedi.");
            return await RenderExamScoresAsync(model, null, cancellationToken);
        }

        TempData["SuccessMessage"] = model.ScoreId > 0
            ? "Sınav sonucunuz güncellendi."
            : "Sınav sonucunuz eklendi.";
        return RedirectToAction(nameof(ExamScores));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteExamScore(int scoreId, CancellationToken cancellationToken)
    {
        if (scoreId <= 0)
        {
            TempData["ErrorMessage"] = "Geçerli bir sınav sonucu seçiniz.";
            return RedirectToAction(nameof(ExamScores));
        }

        var result = await apiClient.DeleteExamScoreAsync(scoreId, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Sınav sonucunuz silindi."
            : result.Error ?? "Sınav sonucu silinemedi.";
        return RedirectToAction(nameof(ExamScores));
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

    private async Task<IActionResult> RenderExamScoresAsync(
        StudentExamScoreInputViewModel form,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var scoresTask = apiClient.GetMyExamScoresAsync(cancellationToken);
        var examsTask = apiClient.GetExamCatalogAsync(cancellationToken);
        await Task.WhenAll(scoresTask, examsTask);
        var scores = await scoresTask;
        var exams = await examsTask;
        return View(nameof(ExamScores), new StudentExamScoresPageViewModel
        {
            Scores = scores.Value ?? [],
            Exams = exams.Value ?? [],
            Form = form,
            ErrorMessage = errorMessage ?? scores.Error ?? exams.Error
        });
    }
}
