using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Web.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminController(GraduateApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Students(
        string? search,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await apiClient.GetAdminStudentsAsync(search, page, pageSize, cancellationToken);
        return View(new AdminStudentListViewModel
        {
            Result = result.Value ?? new PagedResultViewModel<AdminStudentListItemViewModel>
            {
                Page = Math.Max(page, 1),
                PageSize = Math.Clamp(pageSize, 10, 100)
            },
            Search = search,
            ErrorMessage = result.IsSuccess ? null : result.Error
        });
    }

    [HttpGet]
    public async Task<IActionResult> DeactivateStudent(Guid publicId, CancellationToken cancellationToken)
    {
        if (publicId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Öğrenci seçilmedi.";
            return RedirectToAction(nameof(Students));
        }

        var result = await apiClient.GetAdminStudentAsync(publicId, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            TempData["ErrorMessage"] = result.Error ?? "Öğrenci bulunamadı.";
            return RedirectToAction(nameof(Students));
        }

        return View(result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmDeactivateStudent(Guid publicId, CancellationToken cancellationToken)
    {
        if (publicId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Öğrenci seçilmedi.";
            return RedirectToAction(nameof(Students));
        }

        var result = await apiClient.DeactivateStudentAsync(publicId, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Öğrenci pasifleştirildi ve mevcut oturumları iptal edildi."
            : result.Error ?? "Öğrenci pasifleştirilemedi.";
        return RedirectToAction(nameof(Students));
    }

    [HttpGet]
    public async Task<IActionResult> ActivateStudent(Guid publicId, CancellationToken cancellationToken)
    {
        if (publicId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Öğrenci seçilmedi.";
            return RedirectToAction(nameof(Students));
        }

        var result = await apiClient.GetAdminStudentAsync(publicId, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            TempData["ErrorMessage"] = result.Error ?? "Öğrenci bulunamadı.";
            return RedirectToAction(nameof(Students));
        }

        return View(result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmActivateStudent(Guid publicId, CancellationToken cancellationToken)
    {
        if (publicId == Guid.Empty)
        {
            TempData["ErrorMessage"] = "Öğrenci seçilmedi.";
            return RedirectToAction(nameof(Students));
        }

        var result = await apiClient.ActivateStudentAsync(publicId, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Öğrenci yeniden aktifleştirildi ve önceki oturumları geçersiz kılındı."
            : result.Error ?? "Öğrenci aktifleştirilemedi.";
        return RedirectToAction(nameof(Students));
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        ApplicationStatus? status,
        int? academicYearStart,
        AcademicTerm? term,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await apiClient.GetAdminApplicationsAsync(
            search,
            status,
            academicYearStart,
            term,
            page,
            pageSize,
            cancellationToken);
        return View(new AdminApplicationListViewModel
        {
            Result = result.Value ?? new PagedResultViewModel<AdminApplicationListItemViewModel>
            {
                Page = Math.Max(page, 1),
                PageSize = Math.Clamp(pageSize, 10, 100)
            },
            Search = search,
            Status = status,
            AcademicYearStart = academicYearStart,
            Term = term,
            ErrorMessage = result.IsSuccess ? null : result.Error
        });
    }

    [HttpGet]
    public async Task<IActionResult> Offerings(
        int? academicYearStart,
        AcademicTerm? term,
        bool includeArchived = false,
        int? editId = null,
        CancellationToken cancellationToken = default)
    {
        var offeringsTask = apiClient.GetProgramOfferingsAsync(academicYearStart, term, includeArchived, cancellationToken);
        var catalogTask = apiClient.GetProgramOfferingCatalogAsync(cancellationToken);
        await Task.WhenAll(offeringsTask, catalogTask);
        var offerings = await offeringsTask;
        var catalog = await catalogTask;
        var catalogValue = catalog.Value ?? new ProgramOfferingCatalogViewModel();
        var selected = editId.HasValue
            ? offerings.Value?.SingleOrDefault(item => item.ProgramOfferingId == editId.Value)
            : null;

        return View(new ProgramOfferingPageViewModel
        {
            Offerings = offerings.Value ?? [],
            AcademicYearStart = academicYearStart,
            Term = term,
            IncludeArchived = includeArchived,
            Form = CreateOfferingForm(selected, catalogValue),
            ErrorMessage = offerings.IsSuccess && catalog.IsSuccess
                ? null
                : offerings.Error ?? catalog.Error ?? "İlan bilgileri yüklenemedi."
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveOffering(
        [Bind(Prefix = "Form")] ProgramOfferingFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (model.Term == AcademicTerm.LegacyUnspecified)
        {
            ModelState.AddModelError("Form.Term", "Geçerli bir dönem seçiniz.");
        }

        if (model.ApplicationStartLocal >= model.ApplicationDeadlineLocal)
        {
            ModelState.AddModelError(
                "Form.ApplicationDeadlineLocal",
                "Son başvuru tarihi başlangıçtan sonra olmalıdır.");
        }

        if (model.ProgramOfferingId > 0 && string.IsNullOrWhiteSpace(model.RowVersion))
        {
            ModelState.AddModelError("Form.RowVersion", "İlan eş zamanlılık bilgisi eksik. Sayfayı yenileyin.");
        }

        if (!ModelState.IsValid)
        {
            return await RenderOfferingFormAsync(
                model,
                "İlan bilgileri doğrulanamadı.",
                cancellationToken);
        }

        var result = model.ProgramOfferingId > 0
            ? await apiClient.UpdateProgramOfferingAsync(model, cancellationToken)
            : await apiClient.CreateProgramOfferingAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Dönemsel ilan kaydedilemedi.");
            return await RenderOfferingFormAsync(model, null, cancellationToken);
        }

        TempData["SuccessMessage"] = "Dönemsel ilan kaydedildi.";
        return RedirectToAction(nameof(Offerings));
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

    private static ProgramOfferingFormViewModel CreateOfferingForm(
        ProgramOfferingAdminViewModel? offering,
        ProgramOfferingCatalogViewModel catalog)
    {
        var requirements = catalog.Exams.Select(exam =>
        {
            var existing = offering?.ExamRequirements.SingleOrDefault(item => item.ExamId == exam.ExamId);
            return new ProgramOfferingRequirementInputViewModel
            {
                IsConfigured = existing is not null,
                ExamId = exam.ExamId,
                MinimumScore = existing?.MinimumScore ?? 0,
                MinimumValidityDate = existing?.MinimumValidityDate,
                IsRequired = existing?.IsRequired ?? false
            };
        }).ToList();
        var nowLocal = IstanbulTime.FromUtc(DateTime.UtcNow);
        return new ProgramOfferingFormViewModel
        {
            ProgramOfferingId = offering?.ProgramOfferingId ?? 0,
            ProgramId = offering?.ProgramId ?? catalog.Programs.FirstOrDefault()?.ProgramId ?? 0,
            AcademicYearStart = offering?.AcademicYearStart > 0 ? offering.AcademicYearStart : nowLocal.Year,
            Term = offering?.Term is AcademicTerm.Fall or AcademicTerm.Spring or AcademicTerm.Summer
                ? offering.Term
                : AcademicTerm.Fall,
            ApplicationStartLocal = offering?.ApplicationStartUtc is { } start
                ? IstanbulTime.FromUtc(start)
                : nowLocal,
            ApplicationDeadlineLocal = offering?.ApplicationDeadlineUtc is { } deadline
                ? IstanbulTime.FromUtc(deadline)
                : nowLocal.AddMonths(1),
            Quota = offering?.Quota > 0 ? offering.Quota : 1,
            IsOpen = offering?.IsOpen ?? false,
            IsArchived = offering?.IsArchived ?? false,
            RowVersion = offering?.RowVersion ?? string.Empty,
            ExamRequirements = requirements,
            Programs = catalog.Programs,
            Exams = catalog.Exams
        };
    }

    private async Task<IActionResult> RenderOfferingFormAsync(
        ProgramOfferingFormViewModel form,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var offeringsTask = apiClient.GetProgramOfferingsAsync(null, null, true, cancellationToken);
        var catalogTask = apiClient.GetProgramOfferingCatalogAsync(cancellationToken);
        await Task.WhenAll(offeringsTask, catalogTask);
        var offerings = await offeringsTask;
        var catalog = await catalogTask;
        var catalogValue = catalog.Value ?? new ProgramOfferingCatalogViewModel();
        form.Programs = catalogValue.Programs;
        form.Exams = catalogValue.Exams;
        var postedRequirements = form.ExamRequirements
            .GroupBy(item => item.ExamId)
            .ToDictionary(group => group.Key, group => group.First());
        form.ExamRequirements = catalogValue.Exams.Select(exam =>
            postedRequirements.TryGetValue(exam.ExamId, out var requirement)
                ? requirement
                : new ProgramOfferingRequirementInputViewModel { ExamId = exam.ExamId })
            .ToList();

        return View(nameof(Offerings), new ProgramOfferingPageViewModel
        {
            Offerings = offerings.Value ?? [],
            IncludeArchived = true,
            Form = form,
            ErrorMessage = errorMessage
                ?? offerings.Error
                ?? catalog.Error
        });
    }
}
