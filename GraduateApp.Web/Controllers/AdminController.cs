using GraduateApp.Web.Localization;
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
            ErrorMessage = result.IsSuccess ? null : ApiError(result.Error, "Admin.Message.AdminUpdateFailed")
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InviteAdmin(InviteAdminViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = T("Admin.Message.InviteEmailInvalid");
            return RedirectToAction(nameof(Accounts));
        }

        var result = await apiClient.InviteAdminAsync(model, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.InviteCreated")
            : ApiError(result.Error, "Admin.Message.InviteCreateFailed");
        return RedirectToAction(nameof(Accounts));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ResendAdminInvitation(Guid publicId, string rowVersion, CancellationToken cancellationToken) =>
        ChangeAdminAccountAsync(publicId, rowVersion, token => apiClient.ResendAdminInvitationAsync(publicId, rowVersion, token), T("Admin.Message.InviteResent"), cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ActivateAdmin(Guid publicId, string rowVersion, CancellationToken cancellationToken) =>
        ChangeAdminAccountAsync(publicId, rowVersion, token => apiClient.ActivateAdminAsync(publicId, rowVersion, token), T("Admin.Message.AdminActivated"), cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> DeactivateAdmin(Guid publicId, string rowVersion, CancellationToken cancellationToken) =>
        ChangeAdminAccountAsync(publicId, rowVersion, token => apiClient.DeactivateAdminAsync(publicId, rowVersion, token), T("Admin.Message.AdminDeactivated"), cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UnlockAdmin(Guid publicId, string rowVersion, CancellationToken cancellationToken) =>
        ChangeAdminAccountAsync(publicId, rowVersion, token => apiClient.UnlockAdminAsync(publicId, rowVersion, token), T("Admin.Message.AdminUnlocked"), cancellationToken);

    private async Task<IActionResult> ChangeAdminAccountAsync(
        Guid publicId,
        string rowVersion,
        Func<CancellationToken, Task<ApiResult<AdminAccountViewModel>>> operation,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (publicId == Guid.Empty || string.IsNullOrWhiteSpace(rowVersion))
        {
            TempData["ErrorMessage"] = T("Admin.Message.AdminAccountInvalid");
            return RedirectToAction(nameof(Accounts));
        }

        var result = await operation(cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? successMessage
            : ApiError(result.Error, "Admin.Message.AdminUpdateFailed");
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
            ErrorMessage = result.IsSuccess ? null : ApiError(result.Error, "Admin.Message.StudentNotFound")
        });
    }

    [HttpGet]
    public async Task<IActionResult> DeactivateStudent(Guid publicId, CancellationToken cancellationToken)
    {
        if (publicId == Guid.Empty)
        {
            TempData["ErrorMessage"] = T("Admin.Message.StudentNotSelected");
            return RedirectToAction(nameof(Students));
        }

        var result = await apiClient.GetAdminStudentAsync(publicId, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            TempData["ErrorMessage"] = ApiError(result.Error, "Admin.Message.StudentNotFound");
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
            TempData["ErrorMessage"] = T("Admin.Message.StudentNotSelected");
            return RedirectToAction(nameof(Students));
        }

        var result = await apiClient.DeactivateStudentAsync(publicId, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.StudentDeactivated")
            : ApiError(result.Error, "Admin.Message.StudentDeactivateFailed");
        return RedirectToAction(nameof(Students));
    }

    [HttpGet]
    public async Task<IActionResult> ActivateStudent(Guid publicId, CancellationToken cancellationToken)
    {
        if (publicId == Guid.Empty)
        {
            TempData["ErrorMessage"] = T("Admin.Message.StudentNotSelected");
            return RedirectToAction(nameof(Students));
        }

        var result = await apiClient.GetAdminStudentAsync(publicId, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            TempData["ErrorMessage"] = ApiError(result.Error, "Admin.Message.StudentNotFound");
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
            TempData["ErrorMessage"] = T("Admin.Message.StudentNotSelected");
            return RedirectToAction(nameof(Students));
        }

        var result = await apiClient.ActivateStudentAsync(publicId, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.StudentActivated")
            : ApiError(result.Error, "Admin.Message.StudentActivateFailed");
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
            ErrorMessage = result.IsSuccess ? null : ApiError(result.Error, "Admin.Message.ApplicationNotFound")
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
        var offeringValues = offerings.Value ?? [];
        var catalogValue = catalog.Value ?? new ProgramOfferingCatalogViewModel();
        var selected = editId.HasValue
            ? offeringValues.SingleOrDefault(item => item.ProgramOfferingId == editId.Value)
            : null;
        var editOfferingError = editId.HasValue && selected is null
            ? T("Admin.Message.OfferingEditNotFound")
            : null;
        var selectedRequirementOffering = requirementOfferingId.HasValue
            ? offeringValues.SingleOrDefault(item => item.ProgramOfferingId == requirementOfferingId.Value)
            : null;
        var canEditSelectedRequirements = selectedRequirementOffering is not null
            && (!selectedRequirementOffering.UsesEvaluationWorkflow
                || selectedRequirementOffering.EvaluationState == OfferingEvaluationState.Configuring);
        var requirementResults =
            new Dictionary<int, IReadOnlyList<OfferingDocumentRequirementViewModel>>();
        string? documentRequirementError = null;
        if (requirementOfferingId.HasValue)
        {
            if (selectedRequirementOffering is null)
            {
                documentRequirementError = T("Admin.Message.RequirementOfferingNotFound");
            }
            else
            {
                var requirementResult = await apiClient.GetOfferingDocumentRequirementsAsync(
                    selectedRequirementOffering.ProgramOfferingId,
                    cancellationToken);
                if (requirementResult.IsSuccess)
                {
                    requirementResults[selectedRequirementOffering.ProgramOfferingId] =
                        requirementResult.Value ?? [];
                }
                else
                {
                    documentRequirementError =
                        ApiError(requirementResult.Error, "Admin.Message.RequirementsLoadFailed");
                }
            }
        }
        else if (editRequirementId.HasValue)
        {
            documentRequirementError = T("Admin.Message.SelectOfferingBeforeRequirement");
        }

        OfferingDocumentRequirementViewModel? selectedRequirement = null;
        if (canEditSelectedRequirements && selectedRequirementOffering is not null && editRequirementId.HasValue
            && requirementResults.TryGetValue(
                selectedRequirementOffering.ProgramOfferingId,
                out var offeringRequirements))
        {
            selectedRequirement = offeringRequirements.SingleOrDefault(item => item.PublicId == editRequirementId.Value);
            if (selectedRequirement is null)
            {
                documentRequirementError = T("Admin.Message.RequirementNotFound");
            }
        }

        return View(new ProgramOfferingPageViewModel
        {
            Offerings = offeringValues,
            AcademicYearStart = academicYearStart,
            Term = term,
            IncludeArchived = includeArchived,
            Form = CreateOfferingForm(selected, catalogValue),
            DocumentRequirements = requirementResults,
            RequirementOfferingId = selectedRequirementOffering?.ProgramOfferingId,
            AutoFocusTarget = editId.HasValue
                ? "offering-form-heading"
                : requirementOfferingId.HasValue
                    ? "document-requirements-heading"
                    : null,
            DocumentRequirementForm = new OfferingDocumentRequirementFormViewModel
            {
                ProgramOfferingId = canEditSelectedRequirements
                    ? selectedRequirementOffering?.ProgramOfferingId ?? 0
                    : 0,
                PublicId = selectedRequirement?.PublicId ?? Guid.Empty,
                DocumentCode = selectedRequirement?.DocumentCode ?? string.Empty,
                DisplayName = selectedRequirement?.DisplayName ?? string.Empty,
                Description = selectedRequirement?.Description,
                IsRequired = selectedRequirement?.IsRequired ?? true,
                AllowedContentCategory = selectedRequirement?.AllowedContentCategory ?? DocumentContentCategory.PdfOrImage,
                MaximumBytes = selectedRequirement?.MaximumBytes ?? 10485760,
                RowVersion = selectedRequirement?.RowVersion
            },
            ErrorMessage = !offerings.IsSuccess || !catalog.IsSuccess
                ? ApiError(offerings.Error ?? catalog.Error, "Admin.Message.OfferingsLoadFailed")
                : editOfferingError ?? documentRequirementError
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
            ModelState.AddModelError("Form.Term", T("Admin.Message.TermInvalid"));
        }

        if (model.ApplicationStartLocal >= model.ApplicationDeadlineLocal)
        {
            ModelState.AddModelError(
                "Form.ApplicationDeadlineLocal",
                T("Admin.Message.DeadlineAfterStart"));
        }

        if (model.ProgramOfferingId > 0 && string.IsNullOrWhiteSpace(model.RowVersion))
        {
            ModelState.AddModelError("Form.RowVersion", T("Admin.Message.OfferingConcurrencyMissing"));
        }

        if (!ModelState.IsValid)
        {
            return await RenderOfferingFormAsync(
                model,
                T("Admin.Message.OfferingValidationFailed"),
                cancellationToken);
        }

        var result = model.ProgramOfferingId > 0
            ? await apiClient.UpdateProgramOfferingAsync(model, cancellationToken)
            : await apiClient.CreateProgramOfferingAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, ApiError(result.Error, "Admin.Message.OfferingSaveFailed"));
            return await RenderOfferingFormAsync(model, null, cancellationToken);
        }

        TempData["SuccessMessage"] = T("Admin.Message.OfferingSaved");
        var savedOfferingId = result.Value?.ProgramOfferingId ?? model.ProgramOfferingId;
        return RedirectToAction(
            nameof(Offerings),
            controllerName: null,
            routeValues: new
            {
                academicYearStart = model.AcademicYearStart,
                term = model.Term,
                includeArchived = model.IsArchived,
                editId = savedOfferingId
            },
            fragment: "offering-form");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseInvalidOfferingForRemediation(
        int programOfferingId,
        string rowVersion,
        int? academicYearStart,
        AcademicTerm? term,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        if (programOfferingId <= 0 || string.IsNullOrWhiteSpace(rowVersion))
        {
            TempData["ErrorMessage"] = T("Admin.Message.RemediationInvalid");
            return RedirectToAction(
                nameof(Offerings),
                controllerName: null,
                routeValues: new { academicYearStart, term, includeArchived },
                fragment: "offering-configuration-health");
        }

        var result = await apiClient.CloseInvalidProgramOfferingForRemediationAsync(
            programOfferingId,
            rowVersion,
            cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.RemediationClosed")
            : ApiError(result.Error, "Admin.Message.RemediationFailed");
        return RedirectToAction(
            nameof(Offerings),
            controllerName: null,
            routeValues: new { academicYearStart, term, includeArchived },
            fragment: "offering-configuration-health");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDocumentRequirement(
        [Bind(Prefix = "DocumentRequirementForm")] OfferingDocumentRequirementFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (model.ProgramOfferingId <= 0)
        {
            ModelState.AddModelError("DocumentRequirementForm.ProgramOfferingId", T("Admin.Message.ValidOfferingRequired"));
        }

        if (model.PublicId != Guid.Empty && string.IsNullOrWhiteSpace(model.RowVersion))
        {
            ModelState.AddModelError("DocumentRequirementForm.RowVersion", T("Admin.Message.RequirementConcurrencyMissing"));
        }

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = T("Admin.Message.RequirementValidationFailed");
            return RedirectToAction(
                nameof(Offerings),
                controllerName: null,
                routeValues: new { requirementOfferingId = model.ProgramOfferingId },
                fragment: "document-requirements");
        }

        var result = model.PublicId == Guid.Empty
            ? await apiClient.CreateOfferingDocumentRequirementAsync(model, cancellationToken)
            : await apiClient.UpdateOfferingDocumentRequirementAsync(model, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.RequirementSaved")
            : ApiError(result.Error, "Admin.Message.RequirementSaveFailed");
        return RedirectToAction(
            nameof(Offerings),
            controllerName: null,
            routeValues: new { requirementOfferingId = model.ProgramOfferingId },
            fragment: "document-requirements");
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
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = T("Admin.Message.RequirementStateInvalid");
            return RedirectToAction(
                nameof(Offerings),
                controllerName: null,
                routeValues: new { requirementOfferingId = programOfferingId },
                fragment: "document-requirements");
        }

        var result = await apiClient.SetOfferingDocumentRequirementActiveAsync(
            programOfferingId,
            publicId,
            isActive,
            rowVersion,
            cancellationToken);
        if (result.IsSuccess && result.Value is not null)
        {
            TempData["SuccessMessage"] = result.Value.IsActive
                ? T("Admin.Message.RequirementActivated")
                : T("Admin.Message.RequirementDeactivated");
        }
        else
        {
            TempData["ErrorMessage"] = ApiError(result.Error, "Admin.Message.RequirementUpdateFailed");
        }

        return RedirectToAction(
            nameof(Offerings),
            controllerName: null,
            routeValues: new { requirementOfferingId = programOfferingId },
            fragment: "document-requirements");
    }

    [HttpGet]
    public async Task<IActionResult> Universities(CancellationToken cancellationToken)
    {
        var result = await apiClient.GetAdminUniversitiesAsync(cancellationToken);
        return View(new UniversityPageViewModel
        {
            Universities = result.Value ?? [],
            ErrorMessage = result.IsSuccess
                ? null
                : ApiError(result.Error, "Admin.Message.UniversitiesLoadFailed")
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUniversity(
        [Bind(Prefix = "Form")] UniversityFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await RenderUniversityFormAsync(
                model,
                T("Admin.Message.UniversityValidationFailed"),
                cancellationToken);
        }

        var result = await apiClient.CreateUniversityAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, ApiError(result.Error, "Admin.Message.UniversityCreateFailed"));
            return await RenderUniversityFormAsync(model, null, cancellationToken);
        }

        TempData["SuccessMessage"] = T("Admin.Message.UniversityCreated");
        return RedirectToAction(nameof(Universities));
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
                : ApiError(list.Error ?? selectedResult?.Error, "Admin.Message.InstitutesLoadFailed")
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
            ModelState.AddModelError("Form.RowVersion", T("Admin.Message.InstituteConcurrencyMissing"));
        }

        if (!ModelState.IsValid)
        {
            return await RenderInstituteFormAsync(model, T("Admin.Message.InstituteValidationFailed"), cancellationToken);
        }

        var result = model.InstituteId > 0
            ? await apiClient.UpdateInstituteAsync(model, cancellationToken)
            : await apiClient.CreateInstituteAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, ApiError(result.Error, "Admin.Message.InstituteSaveFailed"));
            return await RenderInstituteFormAsync(model, null, cancellationToken);
        }

        TempData["SuccessMessage"] = model.InstituteId > 0 ? T("Admin.Message.InstituteUpdated") : T("Admin.Message.InstituteCreated");
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
            T("Admin.Message.InstituteActivated"),
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
            T("Admin.Message.InstituteDeactivated"),
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
            TempData["ErrorMessage"] = T("Admin.Message.InstituteStateInvalid");
            return RedirectToAction(nameof(Institutes));
        }

        var result = await changeActive(cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? successMessage
            : ApiError(result.Error, "Admin.Message.InstituteStateFailed");
        return RedirectToAction(nameof(Institutes));
    }

    [HttpGet]
    public async Task<IActionResult> DeleteInstitute(int id, CancellationToken cancellationToken)
    {
        var result = await apiClient.GetAdminInstituteAsync(id, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            TempData["ErrorMessage"] = ApiError(result.Error, "Admin.Message.InstituteNotFound");
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
            TempData["ErrorMessage"] = T("Admin.Message.InstituteDeleteInvalid");
            return RedirectToAction(nameof(Institutes));
        }

        var result = await apiClient.DeleteInstituteAsync(id, rowVersion, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.InstituteDeleted")
            : ApiError(result.Error, "Admin.Message.InstituteDeleteFailed");
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
                ProgramNameEnglish = selected?.ProgramNameEnglish,
                DegreeType = selected?.DegreeType ?? ProgramDegreeTypeOptions.Values[0],
                RowVersion = selected?.RowVersion
            },
            ErrorMessage = programs.IsSuccess && institutes.IsSuccess && (selectedResult is null || selectedResult.IsSuccess)
                ? null
                : ApiError(programs.Error ?? institutes.Error ?? selectedResult?.Error, "Admin.Message.ProgramsLoadFailed")
        });
    }

    [HttpGet]
    public async Task<IActionResult> ProgramTranslations(
        string? search,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var result = await apiClient.GetAdminProgramsAsync(
            search,
            null,
            null,
            null,
            page,
            pageSize,
            cancellationToken);
        var value = result.Value;
        return View(new ProgramTranslationPageViewModel
        {
            Items = value?.Items.Select(program => new ProgramTranslationItemViewModel
            {
                ProgramId = program.ProgramId,
                InstituteName = program.InstituteName,
                ProgramName = program.ProgramName,
                DegreeType = program.DegreeType,
                ProgramNameEnglish = program.ProgramNameEnglish,
                RowVersion = program.RowVersion
            }).ToList() ?? [],
            Search = search,
            Page = value?.Page ?? page,
            PageSize = value?.PageSize ?? pageSize,
            TotalCount = value?.TotalCount ?? 0,
            ErrorMessage = result.IsSuccess ? null : ApiError(result.Error, "Admin.Message.TranslationSaveFailed")
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProgramTranslations(
        ProgramTranslationPageViewModel model,
        CancellationToken cancellationToken)
    {
        if (model.Items.Count is < 1 or > 100)
        {
            ModelState.AddModelError(
                nameof(model.Items),
                T("Admin.Message.TranslationBatchRange"));
        }

        if (!ModelState.IsValid)
        {
            model.ErrorMessage = T("Admin.Message.TranslationValidationFailed");
            return View("ProgramTranslations", model);
        }

        var result = await apiClient.UpdateProgramTranslationsAsync(
            model.Items,
            cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(
                string.Empty,
                ApiError(result.Error, "Admin.Message.TranslationSaveFailed"));
            model.ErrorMessage = T("Admin.Message.TranslationSaveFailed");
            return View("ProgramTranslations", model);
        }

        TempData["SuccessMessage"] = result.Value?.UpdatedCount switch
        {
            0 => T("Admin.Message.TranslationNoChanges"),
            1 => T("Admin.Message.TranslationOneUpdated"),
            var count => UiText.Format(HttpContext, "Admin.Message.TranslationManyUpdated", count)
        };
        return RedirectToAction(
            nameof(ProgramTranslations),
            new { model.Search, model.Page, model.PageSize });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProgram(
        [Bind(Prefix = "Form")] ProgramFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (model.ProgramId > 0 && string.IsNullOrWhiteSpace(model.RowVersion))
        {
            ModelState.AddModelError("Form.RowVersion", T("Admin.Message.ProgramConcurrencyMissing"));
        }

        if (!ModelState.IsValid)
        {
            return await RenderProgramFormAsync(model, T("Admin.Message.ProgramValidationFailed"), cancellationToken);
        }

        var result = model.ProgramId > 0
            ? await apiClient.UpdateProgramAsync(model, cancellationToken)
            : await apiClient.CreateProgramAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, ApiError(result.Error, "Admin.Message.ProgramSaveFailed"));
            return await RenderProgramFormAsync(model, null, cancellationToken);
        }

        TempData["SuccessMessage"] = model.ProgramId > 0 ? T("Admin.Message.ProgramUpdated") : T("Admin.Message.ProgramCreated");
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
            T("Admin.Message.ProgramActivated"),
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
            T("Admin.Message.ProgramDeactivated"),
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
            TempData["ErrorMessage"] = T("Admin.Message.ProgramStateInvalid");
            return RedirectToAction(nameof(Programs));
        }

        var result = await changeActive(cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? successMessage
            : ApiError(result.Error, "Admin.Message.ProgramStateFailed");
        return RedirectToAction(nameof(Programs));
    }

    [HttpGet]
    public async Task<IActionResult> DeleteProgram(int id, CancellationToken cancellationToken)
    {
        var result = await apiClient.GetAdminProgramAsync(id, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            TempData["ErrorMessage"] = ApiError(result.Error, "Admin.Message.ProgramNotFound");
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
            TempData["ErrorMessage"] = T("Admin.Message.ProgramDeleteInvalid");
            return RedirectToAction(nameof(Programs));
        }

        var result = await apiClient.DeleteProgramAsync(id, rowVersion, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.ProgramDeleted")
            : ApiError(result.Error, "Admin.Message.ProgramDeleteFailed");
        return RedirectToAction(nameof(Programs));
    }

    [HttpGet]
    public async Task<IActionResult> Evaluation(int id, CancellationToken cancellationToken)
    {
        var evaluationTask = apiClient.GetEvaluationAsync(id, cancellationToken);
        var previewTask = apiClient.GetEvaluationPreviewAsync(id, cancellationToken);
        await Task.WhenAll(evaluationTask, previewTask);
        var evaluation = await evaluationTask;
        var preview = await previewTask;
        if (!evaluation.IsSuccess || evaluation.Value is null)
        {
            TempData["ErrorMessage"] = ApiError(evaluation.Error, "Admin.Message.EvaluationNotFound");
            return RedirectToAction(nameof(Offerings));
        }

        return View(new EvaluationPageViewModel
        {
            Evaluation = evaluation.Value,
            Preview = preview.Value ?? new EvaluationRankingPreviewViewModel(),
            CriterionForm = new EvaluationCriterionFormViewModel
            {
                ProgramOfferingId = id,
                SourceType = EvaluationCriterionSourceType.ManualScore,
                MaximumRawScore = 100m,
                TieBreakPriority = evaluation.Value.Criteria.Count + 1
            },
            ErrorMessage = preview.IsSuccess ? null : ApiError(preview.Error, "Admin.Message.EvaluationFinalizeFailed")
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveEvaluationCriterion(
        EvaluationCriterionFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (model.ProgramOfferingId <= 0
            || (model.PublicId != Guid.Empty && string.IsNullOrWhiteSpace(model.RowVersion))
            || !ModelState.IsValid)
        {
            TempData["ErrorMessage"] = T("Admin.Message.CriterionInvalid");
            return RedirectToAction(nameof(Evaluation), new { id = model.ProgramOfferingId });
        }

        var result = model.PublicId == Guid.Empty
            ? await apiClient.CreateEvaluationCriterionAsync(model, cancellationToken)
            : await apiClient.UpdateEvaluationCriterionAsync(model, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.CriterionSaved")
            : ApiError(result.Error, "Admin.Message.CriterionSaveFailed");
        return RedirectToAction(nameof(Evaluation), new { id = model.ProgramOfferingId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEvaluationCriterion(
        int programOfferingId,
        Guid publicId,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        if (programOfferingId <= 0 || publicId == Guid.Empty || string.IsNullOrWhiteSpace(rowVersion))
        {
            TempData["ErrorMessage"] = T("Admin.Message.CriterionDeleteInvalid");
            return RedirectToAction(nameof(Evaluation), new { id = programOfferingId });
        }

        var result = await apiClient.DeleteEvaluationCriterionAsync(
            programOfferingId,
            publicId,
            rowVersion,
            cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.CriterionDeleted")
            : ApiError(result.Error, "Admin.Message.CriterionDeleteFailed");
        return RedirectToAction(nameof(Evaluation), new { id = programOfferingId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DecideEligibility(
        int programOfferingId,
        Guid applicationPublicId,
        EvaluationEligibilityStatus eligibilityStatus,
        string? ineligibilityReason,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var result = await apiClient.DecideEvaluationEligibilityAsync(
            applicationPublicId,
            eligibilityStatus,
            ineligibilityReason,
            rowVersion,
            cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.EligibilitySaved")
            : ApiError(result.Error, "Admin.Message.EligibilitySaveFailed");
        return RedirectToAction(nameof(Evaluation), new { id = programOfferingId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetManualScore(
        int programOfferingId,
        Guid applicationPublicId,
        Guid criterionPublicId,
        decimal? rawScore,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        if (!rawScore.HasValue)
        {
            TempData["ErrorMessage"] = T("Admin.Message.ManualScoreRequired");
            return RedirectToAction(nameof(Evaluation), new { id = programOfferingId });
        }

        var result = await apiClient.SetManualEvaluationScoreAsync(
            applicationPublicId,
            criterionPublicId,
            rawScore.Value,
            rowVersion,
            cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.ManualScoreSaved")
            : ApiError(result.Error, "Admin.Message.ManualScoreSaveFailed");
        return RedirectToAction(nameof(Evaluation), new { id = programOfferingId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinalizeEvaluation(
        int programOfferingId,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var result = await apiClient.FinalizeEvaluationAsync(programOfferingId, rowVersion, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.EvaluationFinalized")
            : ApiError(result.Error, "Admin.Message.EvaluationFinalizeFailed");
        return RedirectToAction(nameof(Evaluation), new { id = programOfferingId });
    }

    [HttpGet]
    public async Task<IActionResult> PublishEvaluation(int id, CancellationToken cancellationToken)
    {
        var evaluationTask = apiClient.GetEvaluationAsync(id, cancellationToken);
        var summaryTask = apiClient.GetEvaluationPublicationSummaryAsync(id, cancellationToken);
        await Task.WhenAll(evaluationTask, summaryTask);
        var evaluation = await evaluationTask;
        var summary = await summaryTask;
        if (!evaluation.IsSuccess || evaluation.Value is null || !summary.IsSuccess || summary.Value is null)
        {
            TempData["ErrorMessage"] = ApiError(summary.Error ?? evaluation.Error, "Admin.Message.NoPublishableResults");
            return RedirectToAction(nameof(Evaluation), new { id });
        }

        return View(new EvaluationPublishPageViewModel
        {
            ProgramOfferingId = id,
            ProgramName = $"{evaluation.Value.ProgramName} · {evaluation.Value.AcademicYear} · {UiText.LocalizeAcademicTerm(HttpContext, evaluation.Value.TermName)}",
            Evaluation = evaluation.Value,
            Summary = summary.Value
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName(nameof(PublishEvaluation))]
    public async Task<IActionResult> PublishEvaluationConfirmed(
        int programOfferingId,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var result = await apiClient.PublishEvaluationAsync(programOfferingId, rowVersion, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.EvaluationPublished")
            : ApiError(result.Error, "Admin.Message.PublishFailed");
        return RedirectToAction(nameof(Evaluation), new { id = programOfferingId });
    }

    [HttpGet]
    public async Task<IActionResult> Detail(Guid id, CancellationToken cancellationToken)
    {
        var result = await apiClient.GetAdminApplicationDetailAsync(id, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            TempData["ErrorMessage"] = ApiError(result.Error, "Admin.Message.ApplicationNotFound");
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
            TempData["ErrorMessage"] = T("Admin.Message.StatusInvalid");
            return RedirectToAction(nameof(Detail), new { id = model.PublicId });
        }

        var result = await apiClient.UpdateApplicationStatusAsync(model, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.StatusUpdated")
            : ApiError(result.Error, "Admin.Message.StatusUpdateFailed");
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
            TempData["ErrorMessage"] = T("Admin.Message.DocumentReviewInvalid");
            return RedirectToAction(nameof(Detail), new { id = model.ApplicationPublicId });
        }

        var result = await apiClient.ReviewApplicationDocumentAsync(model, cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? (model.ReviewStatus == DocumentReviewStatus.Approved ? T("Admin.Message.DocumentApproved") : T("Admin.Message.DocumentRejected"))
            : ApiError(result.Error, "Admin.Message.DocumentReviewSaveFailed");
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
            TempData["ErrorMessage"] = ApiError(result.Error, "Admin.Message.DocumentDownloadFailed");
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
            UsesEvaluationWorkflow = offering?.UsesEvaluationWorkflow ?? true,
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
            AcademicYearStart = form.AcademicYearStart,
            Term = form.Term,
            IncludeArchived = true,
            Form = form,
            AutoFocusTarget = "offering-form-heading",
            ErrorMessage = errorMessage
                ?? ApiError(offerings.Error, "Admin.Message.OfferingsLoadFailed")
                ?? catalog.Error
        });
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
            ErrorMessage = errorMessage ?? ApiError(list.Error, "Admin.Message.InstitutesLoadFailed")
        });
    }

    private async Task<IActionResult> RenderUniversityFormAsync(
        UniversityFormViewModel form,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var list = await apiClient.GetAdminUniversitiesAsync(cancellationToken);
        return View(nameof(Universities), new UniversityPageViewModel
        {
            Universities = list.Value ?? [],
            Form = form,
            ErrorMessage = errorMessage ?? ApiError(list.Error, "Admin.Message.UniversitiesLoadFailed")
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
            ErrorMessage = errorMessage ?? ApiError(programs.Error ?? institutes.Error, "Admin.Message.ProgramsLoadFailed")
        });
    }

    private string T(string key) => UiText.Get(HttpContext, key);

    private string ApiError(string? apiError, string fallbackKey) =>
        UiText.CurrentCultureName(HttpContext) == UiText.TurkishCultureName
        && !string.IsNullOrWhiteSpace(apiError)
            ? apiError
            : T(fallbackKey);

}
