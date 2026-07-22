using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Web.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminController(GraduateApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Accounts(
        string? search,
        AdminAccountStatus? status,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await apiClient.GetAdminAccountsAsync(search, status, page, pageSize, cancellationToken);
        return View(new AdminAccountPageViewModel
        {
            Result = result.Value ?? new PagedResultViewModel<AdminAccountViewModel>
            {
                Page = Math.Max(page, 1),
                PageSize = Math.Clamp(pageSize, 10, 100)
            },
            Search = search,
            Status = status,
            ErrorMessage = result.IsSuccess ? null : result.Error
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InviteAdmin(InviteAdminViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Yönetici daveti için geçerli bir e-posta adresi giriniz.";
            return RedirectToAction(nameof(Accounts));
        }

        var result = await apiClient.InviteAdminAsync(model, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Yönetici daveti oluşturuldu."
            : result.Error ?? "Yönetici daveti oluşturulamadı.";
        return RedirectToAction(nameof(Accounts));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ResendAdminInvitation(Guid publicId, string rowVersion, CancellationToken cancellationToken) =>
        ChangeAdminAccountAsync(publicId, rowVersion, token => apiClient.ResendAdminInvitationAsync(publicId, rowVersion, token), "Yönetici daveti yeniden oluşturuldu.", cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ActivateAdmin(Guid publicId, string rowVersion, CancellationToken cancellationToken) =>
        ChangeAdminAccountAsync(publicId, rowVersion, token => apiClient.ActivateAdminAsync(publicId, rowVersion, token), "Yönetici hesabı aktifleştirildi.", cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> DeactivateAdmin(Guid publicId, string rowVersion, CancellationToken cancellationToken) =>
        ChangeAdminAccountAsync(publicId, rowVersion, token => apiClient.DeactivateAdminAsync(publicId, rowVersion, token), "Yönetici hesabı pasifleştirildi ve mevcut oturumları iptal edildi.", cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UnlockAdmin(Guid publicId, string rowVersion, CancellationToken cancellationToken) =>
        ChangeAdminAccountAsync(publicId, rowVersion, token => apiClient.UnlockAdminAsync(publicId, rowVersion, token), "Yönetici hesabının kilidi açıldı.", cancellationToken);

    private async Task<IActionResult> ChangeAdminAccountAsync(
        Guid publicId,
        string rowVersion,
        Func<CancellationToken, Task<ApiResult<AdminAccountViewModel>>> operation,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (publicId == Guid.Empty || string.IsNullOrWhiteSpace(rowVersion))
        {
            TempData["ErrorMessage"] = "Yönetici hesap bilgisi geçersiz. Sayfayı yenileyiniz.";
            return RedirectToAction(nameof(Accounts));
        }

        var result = await operation(cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? successMessage
            : result.Error ?? "Yönetici hesabı güncellenemedi.";
        return RedirectToAction(nameof(Accounts));
    }

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
        int? requirementOfferingId = null,
        Guid? editRequirementId = null,
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
        var requirementResults = await LoadDocumentRequirementsAsync(offerings.Value ?? [], cancellationToken);
        OfferingDocumentRequirementViewModel? selectedRequirement = null;
        if (requirementOfferingId.HasValue && editRequirementId.HasValue
            && requirementResults.TryGetValue(requirementOfferingId.Value, out var offeringRequirements))
        {
            selectedRequirement = offeringRequirements.SingleOrDefault(item => item.PublicId == editRequirementId.Value);
        }

        return View(new ProgramOfferingPageViewModel
        {
            Offerings = offerings.Value ?? [],
            AcademicYearStart = academicYearStart,
            Term = term,
            IncludeArchived = includeArchived,
            Form = CreateOfferingForm(selected, catalogValue),
            DocumentRequirements = requirementResults,
            DocumentRequirementForm = new OfferingDocumentRequirementFormViewModel
            {
                ProgramOfferingId = requirementOfferingId
                    ?? selected?.ProgramOfferingId
                    ?? offerings.Value?.FirstOrDefault()?.ProgramOfferingId
                    ?? 0,
                PublicId = selectedRequirement?.PublicId ?? Guid.Empty,
                DocumentCode = selectedRequirement?.DocumentCode ?? string.Empty,
                DisplayName = selectedRequirement?.DisplayName ?? string.Empty,
                Description = selectedRequirement?.Description,
                IsRequired = selectedRequirement?.IsRequired ?? true,
                AllowedContentCategory = selectedRequirement?.AllowedContentCategory ?? DocumentContentCategory.PdfOrImage,
                MaximumBytes = selectedRequirement?.MaximumBytes ?? 10485760,
                RowVersion = selectedRequirement?.RowVersion
            },
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
            ModelState.AddModelError("Form.RowVersion", "İlan eşzamanlılık bilgisi eksik. Sayfayı yenileyiniz.");
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDocumentRequirement(
        [Bind(Prefix = "DocumentRequirementForm")] OfferingDocumentRequirementFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (model.ProgramOfferingId <= 0)
        {
            ModelState.AddModelError("DocumentRequirementForm.ProgramOfferingId", "Geçerli bir ilan seçiniz.");
        }

        if (model.PublicId != Guid.Empty && string.IsNullOrWhiteSpace(model.RowVersion))
        {
            ModelState.AddModelError("DocumentRequirementForm.RowVersion", "Eşzamanlılık bilgisi eksik. Sayfayı yenileyin.");
        }

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Belge koşulu bilgileri doğrulanamadı.";
            return RedirectToAction(nameof(Offerings), new { requirementOfferingId = model.ProgramOfferingId });
        }

        var result = model.PublicId == Guid.Empty
            ? await apiClient.CreateOfferingDocumentRequirementAsync(model, cancellationToken)
            : await apiClient.UpdateOfferingDocumentRequirementAsync(model, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Belge koşulu kaydedildi. Değişiklik yalnızca bundan sonra oluşturulan taslakları etkiler."
            : result.Error ?? "Belge koşulu kaydedilemedi.";
        return RedirectToAction(nameof(Offerings), new { requirementOfferingId = model.ProgramOfferingId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDocumentRequirementActive(
        int programOfferingId,
        Guid publicId,
        bool isActive,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var result = await apiClient.SetOfferingDocumentRequirementActiveAsync(
            programOfferingId,
            publicId,
            isActive,
            rowVersion,
            cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? (isActive ? "Belge koşulu aktifleştirildi." : "Belge koşulu pasifleştirildi; geçmiş snapshot kayıtları korundu.")
            : result.Error ?? "Belge koşulu güncellenemedi.";
        return RedirectToAction(nameof(Offerings), new { requirementOfferingId = programOfferingId });
    }

    [HttpGet]
    public async Task<IActionResult> Institutes(
        string? search,
        bool? isActive,
        int page = 1,
        int pageSize = 20,
        int? editId = null,
        CancellationToken cancellationToken = default)
    {
        var list = await apiClient.GetAdminInstitutesAsync(search, isActive, page, pageSize, cancellationToken);
        ApiResult<InstituteAdminViewModel>? selectedResult = null;
        if (editId.HasValue)
        {
            selectedResult = await apiClient.GetAdminInstituteAsync(editId.Value, cancellationToken);
        }
        var selected = selectedResult?.Value;
        return View(new InstitutePageViewModel
        {
            Result = list.Value ?? new PagedResultViewModel<InstituteAdminViewModel>
            {
                Page = Math.Max(page, 1),
                PageSize = Math.Clamp(pageSize, 10, 100)
            },
            Search = search,
            IsActive = isActive,
            Form = new InstituteFormViewModel
            {
                InstituteId = selected?.InstituteId ?? 0,
                InstituteName = selected?.InstituteName ?? string.Empty,
                RowVersion = selected?.RowVersion
            },
            ErrorMessage = list.IsSuccess && (selectedResult is null || selectedResult.IsSuccess)
                ? null
                : list.Error ?? selectedResult?.Error ?? "Enstitü bilgileri yüklenemedi."
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveInstitute(
        [Bind(Prefix = "Form")] InstituteFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (model.InstituteId > 0 && string.IsNullOrWhiteSpace(model.RowVersion))
        {
            ModelState.AddModelError("Form.RowVersion", "Enstitü eşzamanlılık bilgisi eksik. Sayfayı yenileyiniz.");
        }

        if (!ModelState.IsValid)
        {
            return await RenderInstituteFormAsync(model, "Enstitü bilgileri doğrulanamadı.", cancellationToken);
        }

        var result = model.InstituteId > 0
            ? await apiClient.UpdateInstituteAsync(model, cancellationToken)
            : await apiClient.CreateInstituteAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Enstitü kaydedilemedi.");
            return await RenderInstituteFormAsync(model, null, cancellationToken);
        }

        TempData["SuccessMessage"] = model.InstituteId > 0 ? "Enstitü güncellendi." : "Enstitü oluşturuldu.";
        return RedirectToAction(nameof(Institutes));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ActivateInstitute(
        int id,
        string rowVersion,
        CancellationToken cancellationToken) =>
        ChangeInstituteActiveAsync(
            id,
            rowVersion,
            token => apiClient.ActivateInstituteAsync(id, rowVersion, token),
            "Enstitü aktifleştirildi.",
            cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> DeactivateInstitute(
        int id,
        string rowVersion,
        CancellationToken cancellationToken) =>
        ChangeInstituteActiveAsync(
            id,
            rowVersion,
            token => apiClient.DeactivateInstituteAsync(id, rowVersion, token),
            "Enstitü pasifleştirildi. Bağlı programlar yeni seçimlerde gösterilmeyecek.",
            cancellationToken);

    private async Task<IActionResult> ChangeInstituteActiveAsync(
        int id,
        string rowVersion,
        Func<CancellationToken, Task<ApiResult<InstituteAdminViewModel>>> changeActive,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(rowVersion))
        {
            TempData["ErrorMessage"] = "Enstitü aktiflik bilgisi geçersiz. Sayfayı yenileyiniz.";
            return RedirectToAction(nameof(Institutes));
        }

        var result = await changeActive(cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? successMessage
            : result.Error ?? "Enstitü durumu değiştirilemedi.";
        return RedirectToAction(nameof(Institutes));
    }

    [HttpGet]
    public async Task<IActionResult> DeleteInstitute(int id, CancellationToken cancellationToken)
    {
        var result = await apiClient.GetAdminInstituteAsync(id, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            TempData["ErrorMessage"] = result.Error ?? "Enstitü bulunamadı.";
            return RedirectToAction(nameof(Institutes));
        }

        return View(result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmDeleteInstitute(
        int id,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(rowVersion))
        {
            TempData["ErrorMessage"] = "Enstitü silme bilgisi geçersiz. Sayfayı yenileyiniz.";
            return RedirectToAction(nameof(Institutes));
        }

        var result = await apiClient.DeleteInstituteAsync(id, rowVersion, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Kullanılmamış enstitü kalıcı olarak silindi."
            : result.Error ?? "Enstitü silinemedi.";
        return RedirectToAction(nameof(Institutes));
    }

    [HttpGet]
    public async Task<IActionResult> Programs(
        string? search,
        bool? isActive,
        int? instituteId,
        string? degreeType,
        int page = 1,
        int pageSize = 20,
        int? editId = null,
        CancellationToken cancellationToken = default)
    {
        var programsTask = apiClient.GetAdminProgramsAsync(
            search,
            isActive,
            instituteId,
            degreeType,
            page,
            pageSize,
            cancellationToken);
        var institutesTask = apiClient.GetAdminInstitutesAsync(null, null, 1, 100, cancellationToken);
        await Task.WhenAll(programsTask, institutesTask);
        var programs = await programsTask;
        var institutes = await institutesTask;
        ApiResult<ProgramAdminViewModel>? selectedResult = null;
        if (editId.HasValue)
        {
            selectedResult = await apiClient.GetAdminProgramAsync(editId.Value, cancellationToken);
        }
        var selected = selectedResult?.Value;
        var instituteItems = institutes.Value?.Items ?? [];
        return View(new ProgramPageViewModel
        {
            Result = programs.Value ?? new PagedResultViewModel<ProgramAdminViewModel>
            {
                Page = Math.Max(page, 1),
                PageSize = Math.Clamp(pageSize, 10, 100)
            },
            Institutes = instituteItems,
            Search = search,
            IsActive = isActive,
            InstituteId = instituteId,
            DegreeType = degreeType,
            Form = new ProgramFormViewModel
            {
                ProgramId = selected?.ProgramId ?? 0,
                InstituteId = selected?.InstituteId ?? instituteItems.FirstOrDefault()?.InstituteId ?? 0,
                ProgramName = selected?.ProgramName ?? string.Empty,
                DegreeType = selected?.DegreeType ?? ProgramDegreeTypeOptions.Values[0],
                RowVersion = selected?.RowVersion
            },
            ErrorMessage = programs.IsSuccess && institutes.IsSuccess && (selectedResult is null || selectedResult.IsSuccess)
                ? null
                : programs.Error ?? institutes.Error ?? selectedResult?.Error ?? "Program bilgileri yüklenemedi."
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProgram(
        [Bind(Prefix = "Form")] ProgramFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (model.ProgramId > 0 && string.IsNullOrWhiteSpace(model.RowVersion))
        {
            ModelState.AddModelError("Form.RowVersion", "Program eşzamanlılık bilgisi eksik. Sayfayı yenileyiniz.");
        }

        if (!ModelState.IsValid)
        {
            return await RenderProgramFormAsync(model, "Program bilgileri doğrulanamadı.", cancellationToken);
        }

        var result = model.ProgramId > 0
            ? await apiClient.UpdateProgramAsync(model, cancellationToken)
            : await apiClient.CreateProgramAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Program kaydedilemedi.");
            return await RenderProgramFormAsync(model, null, cancellationToken);
        }

        TempData["SuccessMessage"] = model.ProgramId > 0 ? "Program güncellendi." : "Program oluşturuldu.";
        return RedirectToAction(nameof(Programs));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ActivateProgram(
        int id,
        string rowVersion,
        CancellationToken cancellationToken) =>
        ChangeProgramActiveAsync(
            id,
            rowVersion,
            token => apiClient.ActivateProgramAsync(id, rowVersion, token),
            "Program aktifleştirildi.",
            cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> DeactivateProgram(
        int id,
        string rowVersion,
        CancellationToken cancellationToken) =>
        ChangeProgramActiveAsync(
            id,
            rowVersion,
            token => apiClient.DeactivateProgramAsync(id, rowVersion, token),
            "Program pasifleştirildi. Mevcut ilan ve başvurular korunuyor.",
            cancellationToken);

    private async Task<IActionResult> ChangeProgramActiveAsync(
        int id,
        string rowVersion,
        Func<CancellationToken, Task<ApiResult<ProgramAdminViewModel>>> changeActive,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(rowVersion))
        {
            TempData["ErrorMessage"] = "Program aktiflik bilgisi geçersiz. Sayfayı yenileyiniz.";
            return RedirectToAction(nameof(Programs));
        }

        var result = await changeActive(cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? successMessage
            : result.Error ?? "Program durumu değiştirilemedi.";
        return RedirectToAction(nameof(Programs));
    }

    [HttpGet]
    public async Task<IActionResult> DeleteProgram(int id, CancellationToken cancellationToken)
    {
        var result = await apiClient.GetAdminProgramAsync(id, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            TempData["ErrorMessage"] = result.Error ?? "Program bulunamadı.";
            return RedirectToAction(nameof(Programs));
        }

        return View(result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmDeleteProgram(
        int id,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(rowVersion))
        {
            TempData["ErrorMessage"] = "Program silme bilgisi geçersiz. Sayfayı yenileyiniz.";
            return RedirectToAction(nameof(Programs));
        }

        var result = await apiClient.DeleteProgramAsync(id, rowVersion, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "İlanı bulunmayan program kalıcı olarak silindi."
            : result.Error ?? "Program silinemedi.";
        return RedirectToAction(nameof(Programs));
    }

    [HttpGet]
    public async Task<IActionResult> Detail(Guid id, CancellationToken cancellationToken)
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
            return RedirectToAction(nameof(Detail), new { id = model.PublicId });
        }

        var result = await apiClient.UpdateApplicationStatusAsync(model, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? "Başvuru durumu güncellendi."
            : result.Error ?? "Başvuru durumu güncellenemedi.";
        return RedirectToAction(nameof(Detail), new { id = model.PublicId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewDocument(
        ReviewApplicationDocumentViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Belge inceleme bilgileri geçersiz.";
            return RedirectToAction(nameof(Detail), new { id = model.ApplicationPublicId });
        }

        var result = await apiClient.ReviewApplicationDocumentAsync(model, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? (model.ReviewStatus == DocumentReviewStatus.Approved ? "Belge onaylandı." : "Belge gerekçeyle reddedildi.")
            : result.Error ?? "Belge incelemesi kaydedilemedi.";
        return RedirectToAction(nameof(Detail), new { id = model.ApplicationPublicId });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadDocument(
        Guid applicationPublicId,
        Guid documentPublicId,
        CancellationToken cancellationToken)
    {
        var result = await apiClient.DownloadAdminDocumentAsync(applicationPublicId, documentPublicId, cancellationToken);
        if (!result.IsSuccess || result.Content is null)
        {
            TempData["ErrorMessage"] = result.Error ?? "Belge indirilemedi.";
            return RedirectToAction(nameof(Detail), new { id = applicationPublicId });
        }

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(result.Content, result.ContentType!, result.FileName!, enableRangeProcessing: false);
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
            RowVersion = offering?.RowVersion,
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

    private async Task<IReadOnlyDictionary<int, IReadOnlyList<OfferingDocumentRequirementViewModel>>> LoadDocumentRequirementsAsync(
        IReadOnlyList<ProgramOfferingAdminViewModel> offerings,
        CancellationToken cancellationToken)
    {
        var tasks = offerings.ToDictionary(
            item => item.ProgramOfferingId,
            item => apiClient.GetOfferingDocumentRequirementsAsync(item.ProgramOfferingId, cancellationToken));
        await Task.WhenAll(tasks.Values);
        return tasks.ToDictionary(
            item => item.Key,
            item => item.Value.Result.Value ?? (IReadOnlyList<OfferingDocumentRequirementViewModel>)[]);
    }

    private async Task<IActionResult> RenderInstituteFormAsync(
        InstituteFormViewModel form,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var list = await apiClient.GetAdminInstitutesAsync(null, null, 1, 20, cancellationToken);
        return View(nameof(Institutes), new InstitutePageViewModel
        {
            Result = list.Value ?? new PagedResultViewModel<InstituteAdminViewModel> { Page = 1, PageSize = 20 },
            Form = form,
            ErrorMessage = errorMessage ?? list.Error
        });
    }

    private async Task<IActionResult> RenderProgramFormAsync(
        ProgramFormViewModel form,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var programsTask = apiClient.GetAdminProgramsAsync(null, null, null, null, 1, 20, cancellationToken);
        var institutesTask = apiClient.GetAdminInstitutesAsync(null, null, 1, 100, cancellationToken);
        await Task.WhenAll(programsTask, institutesTask);
        var programs = await programsTask;
        var institutes = await institutesTask;
        return View(nameof(Programs), new ProgramPageViewModel
        {
            Result = programs.Value ?? new PagedResultViewModel<ProgramAdminViewModel> { Page = 1, PageSize = 20 },
            Institutes = institutes.Value?.Items ?? [],
            Form = form,
            ErrorMessage = errorMessage ?? programs.Error ?? institutes.Error
        });
    }
}
