using System.Net;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Web.Controllers;

[Authorize(Roles = "Student")]
public sealed class PanelController(GraduateApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        int? academicYearStart,
        AcademicTerm? term,
        bool preferUndergraduateProgram,
        CancellationToken cancellationToken)
    {
        var programSearch = OpenProgramSearchViewModel.From(
            search,
            academicYearStart,
            term,
            preferUndergraduateProgram);
        var applicationsTask = apiClient.GetMyApplicationsAsync(cancellationToken);
        if (!TryValidateModel(programSearch, nameof(PanelDashboardViewModel.ProgramSearch)))
        {
            var invalidSearchApplications = await applicationsTask;
            return View(new PanelDashboardViewModel
            {
                ProgramSearch = programSearch,
                Applications = invalidSearchApplications.Value ?? [],
                ErrorMessage = invalidSearchApplications.IsSuccess
                    ? null
                    : invalidSearchApplications.Error ?? "Başvurular yüklenemedi."
            });
        }

        var programsTask = apiClient.GetOpenProgramsAsync(
            programSearch.Search,
            programSearch.AcademicYearStart,
            programSearch.Term,
            cancellationToken);
        var profileTask = programSearch.PreferUndergraduateProgram
            ? apiClient.GetProfileAsync(cancellationToken)
            : null;

        await Task.WhenAll(programsTask, applicationsTask);
        var programs = await programsTask;
        var applications = await applicationsTask;
        var openPrograms = (programs.Value ?? []).ToArray();
        string? graduatedProgram = null;
        string? recommendationMessage = null;

        if (profileTask is not null)
        {
            var profile = await profileTask;
            if (!programs.IsSuccess)
            {
                recommendationMessage =
                    "Programlar yüklenemediği için profil önerileri şu anda kullanılamıyor.";
            }
            else if (!profile.IsSuccess || profile.Value is null)
            {
                recommendationMessage =
                    "Eğitim profiliniz yüklenemediği için programlar normal sıralamada gösteriliyor.";
            }
            else
            {
                graduatedProgram = profile.Value.Education?.GraduatedProgram;
                if (string.IsNullOrWhiteSpace(graduatedProgram))
                {
                    recommendationMessage =
                        "Önerileri kullanmak için profilinizde mezun olduğunuz lisans programını belirtin.";
                }
                else
                {
                    foreach (var program in openPrograms)
                    {
                        program.IsRecommendedForProfile =
                            UndergraduateProgramRecommendation.IsProgramAreaMatch(
                                graduatedProgram,
                                program.ProgramName,
                                program.DegreeType);
                    }

                    var recommendationCount = openPrograms.Count(item => item.IsRecommendedForProfile);
                    openPrograms = openPrograms
                        .OrderByDescending(item => item.IsRecommendedForProfile)
                        .ToArray();
                    recommendationMessage = recommendationCount > 0
                        ? $"{recommendationCount} ilan mezuniyet programınızla aynı alan adına sahip ve listenin başına taşındı."
                        : "Mezuniyet programınızla aynı alan adına sahip açık ilan bulunamadı; diğer ilanlar başvuruya açık olmaya devam ediyor.";
                }
            }
        }

        return View(new PanelDashboardViewModel
        {
            ProgramSearch = programSearch,
            OpenPrograms = openPrograms,
            Applications = applications.Value ?? [],
            GraduatedProgram = graduatedProgram,
            RecommendationMessage = recommendationMessage,
            ErrorMessage = programs.IsSuccess && applications.IsSuccess
                ? null
                : programs.Error ?? applications.Error ?? "Panel bilgileri yüklenemedi."
        });
    }

    [NonAction]
    public Task<IActionResult> Index(
        string? search,
        int? academicYearStart,
        AcademicTerm? term,
        CancellationToken cancellationToken) =>
        Index(search, academicYearStart, term, false, cancellationToken);

    [NonAction]
    public Task<IActionResult> Index(CancellationToken cancellationToken) =>
        Index(null, null, null, false, cancellationToken);

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
            ? "Başvuru taslağınız oluşturuldu. Zorunlu belgeleri yükledikten sonra başvuruyu gönderin."
            : result.Error ?? "Başvuru oluşturulamadı.";
        return result.IsSuccess && result.Value is not null
            ? RedirectToAction(nameof(ApplicationDetail), new { publicId = result.Value.PublicId })
            : RedirectToAction(nameof(Index));
    }

    [HttpGet("Panel/Applications/{publicId:guid}")]
    public async Task<IActionResult> ApplicationDetail(Guid publicId, CancellationToken cancellationToken)
    {
        var result = await apiClient.GetMyApplicationAsync(publicId, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ApplicationDetailFailure(result.StatusCode);
        }

        if (result.Value.UsesEvaluationWorkflow
            && result.Value.CurrentStatus != ApplicationStatus.Withdrawn)
        {
            var published = await apiClient.GetMyPublishedEvaluationAsync(publicId, cancellationToken);
            if (published.IsSuccess)
            {
                result.Value.PublishedEvaluation = published.Value;
            }
        }

        return View(result.Value);
    }

    private IActionResult ApplicationDetailFailure(HttpStatusCode? statusCode)
    {
        if (statusCode == HttpStatusCode.NotFound)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            ViewData["StatusCode"] = StatusCodes.Status404NotFound;
            ViewData["StatusMessage"] = "Başvuru bulunamadı.";
            return View("~/Views/Home/StatusCode.cshtml");
        }

        if (statusCode == HttpStatusCode.Unauthorized)
        {
            var returnUrl = Request.GetEncodedPathAndQuery();
            return RedirectToAction(
                "Login",
                "Account",
                new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
        }

        if (statusCode is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var numericStatusCode = (int)statusCode;
        return StatusCode(numericStatusCode is >= 400 and <= 599
            ? numericStatusCode
            : StatusCodes.Status502BadGateway);
    }

    [HttpPost("Panel/Applications/{publicId:guid}/Documents/{requirementPublicId:guid}/Upload")]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = 104923136)]
    [RequestSizeLimit(104923136)]
    public async Task<IActionResult> UploadDocument(
        Guid publicId,
        Guid requirementPublicId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
        {
            TempData["ErrorMessage"] = "Yüklenecek dosyayı seçiniz.";
            return RedirectToAction(nameof(ApplicationDetail), new { publicId });
        }

        var result = await apiClient.UploadApplicationDocumentAsync(publicId, requirementPublicId, file, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Belge güvenli biçimde yüklendi ve inceleme bekliyor."
            : result.Error ?? "Belge yüklenemedi.";
        return RedirectToAction(nameof(ApplicationDetail), new { publicId });
    }

    [HttpPost("Panel/Applications/{publicId:guid}/Submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitApplication(Guid publicId, CancellationToken cancellationToken)
    {
        var result = await apiClient.SubmitApplicationAsync(publicId, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Başvurunuz başarıyla gönderildi."
            : result.Error ?? "Başvuru gönderilemedi.";
        return RedirectToAction(nameof(ApplicationDetail), new { publicId });
    }

    [HttpPost("Panel/Applications/{publicId:guid}/Withdraw")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WithdrawApplication(
        Guid publicId,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var result = await apiClient.WithdrawApplicationAsync(publicId, rowVersion, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Başvurunuz geri çekildi."
            : result.Error ?? "Başvuru geri çekilemedi.";
        return RedirectToAction(nameof(ApplicationDetail), new { publicId });
    }

    [HttpPost("Panel/Applications/{publicId:guid}/Reactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReactivateApplication(
        Guid publicId,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var result = await apiClient.ReactivateApplicationAsync(publicId, rowVersion, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Başvurunuz taslak olarak yeniden etkinleştirildi. Güncel bilgileri kontrol edip tekrar gönderebilirsiniz."
            : result.Error ?? "Başvuru yeniden etkinleştirilemedi.";
        return RedirectToAction(nameof(ApplicationDetail), new { publicId });
    }

    [HttpGet("Panel/Applications/{publicId:guid}/Documents/{documentPublicId:guid}/Download")]
    public async Task<IActionResult> DownloadDocument(
        Guid publicId,
        Guid documentPublicId,
        CancellationToken cancellationToken)
    {
        var result = await apiClient.DownloadMyDocumentAsync(publicId, documentPublicId, cancellationToken);
        if (!result.IsSuccess || result.Content is null)
        {
            TempData["ErrorMessage"] = result.Error ?? "Belge indirilemedi.";
            return RedirectToAction(nameof(ApplicationDetail), new { publicId });
        }

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(result.Content, result.ContentType!, result.FileName!, enableRangeProcessing: false);
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

        var model = MapProfile(profile.Value, universities.Value ?? []);
        ApplyUniversityCatalogResult(model, universities);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(StudentProfileViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ApplyUniversityCatalogResult(
                model,
                await apiClient.GetUniversitiesAsync(cancellationToken));
            return View(model);
        }

        var result = await apiClient.UpdateProfileAsync(model, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Profil güncellenemedi.");
            ApplyUniversityCatalogResult(
                model,
                await apiClient.GetUniversitiesAsync(cancellationToken));
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

    private static void ApplyUniversityCatalogResult(
        StudentProfileViewModel model,
        ApiResult<IReadOnlyList<UniversityViewModel>> universities)
    {
        model.Universities = universities.Value ?? [];
        model.UniversityCatalogLoadSucceeded = universities.IsSuccess;
        model.UniversityCatalogErrorMessage = universities.IsSuccess
            ? null
            : universities.Error ?? "Üniversite kataloğu yüklenemedi.";
    }

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
