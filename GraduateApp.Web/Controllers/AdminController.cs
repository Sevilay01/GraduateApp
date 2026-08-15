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
        if (editId.HasValue)
        {
            return RedirectToAction(nameof(EditOffering), new { id = editId.Value });
        }

        if (requirementOfferingId.HasValue)
        {
            return RedirectToAction(
                nameof(DocumentRequirements),
                new { id = requirementOfferingId.Value, editRequirementId });
        }

        var offerings = await apiClient.GetProgramOfferingsAsync(
            academicYearStart,
            term,
            includeArchived,
            cancellationToken);
        return View(new ProgramOfferingListPageViewModel
        {
            Offerings = offerings.Value ?? [],
            AcademicYearStart = academicYearStart,
            Term = term,
            IncludeArchived = includeArchived,
            ErrorMessage = offerings.IsSuccess
                ? null
                : ApiError(offerings.Error, "Admin.Message.OfferingsLoadFailed")
        });
    }

    [HttpGet]
    public async Task<IActionResult> OfferingOverview(int id, CancellationToken cancellationToken)
    {
        var result = await apiClient.GetProgramOfferingAsync(id, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            TempData["ErrorMessage"] = ApiError(result.Error, "Admin.Message.OfferingEditNotFound");
            return RedirectToAction(nameof(Offerings));
        }

        var offering = result.Value;
        return View(new OfferingOverviewPageViewModel
        {
            Shell = CreateOfferingShell(offering, OfferingSection.Overview),
            ApplicationStartUtc = offering.ApplicationStartUtc,
            ApplicationDeadlineUtc = offering.ApplicationDeadlineUtc,
            Quota = offering.Quota,
            DocumentRequirementCount = offering.DocumentRequirementCount,
            ActiveRequiredDocumentRequirementCount = offering.ActiveRequiredDocumentRequirementCount,
            DraftApplicationCount = offering.DraftApplicationCount,
            SubmittedOrLaterApplicationCount = offering.SubmittedOrLaterApplicationCount,
            RowVersion = offering.RowVersion
        });
    }

    [HttpGet]
    public async Task<IActionResult> EditOffering(int? id, CancellationToken cancellationToken)
    {
        var catalogTask = apiClient.GetProgramOfferingCatalogAsync(cancellationToken);
        var offeringTask = id.HasValue
            ? apiClient.GetProgramOfferingAsync(id.Value, cancellationToken)
            : null;
        if (offeringTask is not null)
        {
            await Task.WhenAll(catalogTask, offeringTask);
        }

        var catalog = await catalogTask;
        var offering = offeringTask is null ? null : await offeringTask;
        if (id.HasValue && (offering is null || !offering.IsSuccess || offering.Value is null))
        {
            TempData["ErrorMessage"] = ApiError(offering?.Error, "Admin.Message.OfferingEditNotFound");
            return RedirectToAction(nameof(Offerings));
        }

        var selected = offering?.Value;
        return View(new OfferingEditPageViewModel
        {
            Shell = selected is null ? null : CreateOfferingShell(selected, OfferingSection.Edit),
            Form = CreateOfferingEditForm(selected),
            Programs = catalog.Value?.Programs ?? [],
            HasActiveRequiredDocumentRequirement = selected?.HasActiveRequiredDocumentRequirement ?? false,
            ErrorMessage = catalog.IsSuccess
                ? null
                : ApiError(catalog.Error, "Admin.Message.OfferingsLoadFailed")
        });
    }

    [HttpGet]
    public async Task<IActionResult> ExamRequirements(int id, CancellationToken cancellationToken)
    {
        var offeringTask = apiClient.GetProgramOfferingAsync(id, cancellationToken);
        var catalogTask = apiClient.GetProgramOfferingCatalogAsync(cancellationToken);
        await Task.WhenAll(offeringTask, catalogTask);
        var offering = await offeringTask;
        var catalog = await catalogTask;
        if (!offering.IsSuccess || offering.Value is null)
        {
            TempData["ErrorMessage"] = ApiError(offering.Error, "Admin.Message.OfferingEditNotFound");
            return RedirectToAction(nameof(Offerings));
        }

        return View(CreateExamRequirementsPage(
            offering.Value,
            catalog.Value ?? new ProgramOfferingCatalogViewModel(),
            null,
            catalog.IsSuccess ? null : ApiError(catalog.Error, "Admin.Message.OfferingsLoadFailed")));
    }

    [HttpGet]
    public async Task<IActionResult> DocumentRequirements(
        int id,
        Guid? editRequirementId,
        CancellationToken cancellationToken)
    {
        var offeringTask = apiClient.GetProgramOfferingAsync(id, cancellationToken);
        var requirementsTask = apiClient.GetOfferingDocumentRequirementsAsync(id, cancellationToken);
        await Task.WhenAll(offeringTask, requirementsTask);
        var offering = await offeringTask;
        var requirements = await requirementsTask;
        if (!offering.IsSuccess || offering.Value is null)
        {
            TempData["ErrorMessage"] = ApiError(offering.Error, "Admin.Message.RequirementOfferingNotFound");
            return RedirectToAction(nameof(Offerings));
        }

        return View(CreateDocumentRequirementsPage(
            offering.Value,
            requirements.Value ?? [],
            editRequirementId,
            null,
            !requirements.IsSuccess
                ? ApiError(requirements.Error, "Admin.Message.RequirementsLoadFailed")
                : editRequirementId.HasValue
                    ? T("Admin.Message.RequirementNotFound")
                    : null));
    }

    [HttpGet]
    public async Task<IActionResult> OfferingApplications(
        int id,
        string? search,
        ApplicationStatus? status,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var offeringTask = apiClient.GetProgramOfferingAsync(id, cancellationToken);
        var applicationsTask = apiClient.GetOfferingApplicationsAsync(
            id,
            search,
            status,
            page,
            pageSize,
            cancellationToken);
        await Task.WhenAll(offeringTask, applicationsTask);
        var offering = await offeringTask;
        var applications = await applicationsTask;
        if (!offering.IsSuccess || offering.Value is null)
        {
            TempData["ErrorMessage"] = ApiError(offering.Error, "Admin.Message.OfferingEditNotFound");
            return RedirectToAction(nameof(Offerings));
        }

        return View(new OfferingApplicationsPageViewModel
        {
            Shell = CreateOfferingShell(offering.Value, OfferingSection.Applications),
            Result = applications.Value ?? new PagedResultViewModel<AdminApplicationListItemViewModel>
            {
                Page = Math.Max(page, 1),
                PageSize = Math.Clamp(pageSize, 10, 100)
            },
            Search = search,
            Status = status,
            ErrorMessage = applications.IsSuccess
                ? null
                : ApiError(applications.Error, "Admin.Message.ApplicationNotFound")
        });
    }

    [HttpGet]
    public async Task<IActionResult> EvaluationCriteria(int id, CancellationToken cancellationToken)
    {
        var offeringTask = apiClient.GetProgramOfferingAsync(id, cancellationToken);
        var criteriaTask = apiClient.GetEvaluationCriteriaAsync(id, cancellationToken);
        await Task.WhenAll(offeringTask, criteriaTask);
        var offering = await offeringTask;
        var criteria = await criteriaTask;
        if (!offering.IsSuccess || offering.Value is null || !offering.Value.UsesEvaluationWorkflow)
        {
            TempData["ErrorMessage"] = ApiError(offering.Error, "Admin.Message.EvaluationNotFound");
            return RedirectToAction(nameof(Offerings));
        }

        var items = criteria.Value ?? [];
        return View(new EvaluationCriteriaPageViewModel
        {
            Shell = CreateOfferingShell(offering.Value, OfferingSection.EvaluationCriteria),
            Criteria = items,
            EligibleExamRequirements = offering.Value.ExamRequirements.Where(item => item.IsRequired).ToArray(),
            CriterionForm = new EvaluationCriterionFormViewModel
            {
                ProgramOfferingId = id,
                SourceType = EvaluationCriterionSourceType.ManualScore,
                MaximumRawScore = 100m,
                TieBreakPriority = items.Count + 1
            },
            CanEdit = offering.Value.EvaluationState == OfferingEvaluationState.Configuring
                && !offering.Value.IsOpen
                && offering.Value.DraftApplicationCount + offering.Value.SubmittedOrLaterApplicationCount == 0,
            ErrorMessage = criteria.IsSuccess
                ? null
                : ApiError(criteria.Error, "Admin.Message.EvaluationNotFound")
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveOffering(
        [Bind(Prefix = "Form")] OfferingEditFormViewModel model,
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
            return await RenderOfferingEditFormAsync(
                model,
                T("Admin.Message.OfferingValidationFailed"),
                cancellationToken);
        }

        ProgramOfferingAdminViewModel? existing = null;
        if (model.ProgramOfferingId > 0)
        {
            var existingResult = await apiClient.GetProgramOfferingAsync(model.ProgramOfferingId, cancellationToken);
            if (!existingResult.IsSuccess || existingResult.Value is null)
            {
                ModelState.AddModelError(string.Empty, ApiError(existingResult.Error, "Admin.Message.OfferingEditNotFound"));
                return await RenderOfferingEditFormAsync(model, null, cancellationToken);
            }

            existing = existingResult.Value;
        }

        var apiModel = CreateOfferingApiForm(model, existing?.ExamRequirements ?? []);
        var result = model.ProgramOfferingId > 0
            ? await apiClient.UpdateProgramOfferingAsync(apiModel, cancellationToken)
            : await apiClient.CreateProgramOfferingAsync(apiModel, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, ApiError(result.Error, "Admin.Message.OfferingSaveFailed"));
            return await RenderOfferingEditFormAsync(model, null, cancellationToken);
        }

        TempData["SuccessMessage"] = T("Admin.Message.OfferingSaved");
        var savedOfferingId = result.Value?.ProgramOfferingId ?? model.ProgramOfferingId;
        return RedirectToAction(nameof(OfferingOverview), new { id = savedOfferingId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveExamRequirements(
        [Bind(Prefix = "Form")] OfferingExamRequirementsFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (model.ProgramOfferingId <= 0)
        {
            ModelState.AddModelError("Form.ProgramOfferingId", T("Admin.Message.ValidOfferingRequired"));
        }

        if (string.IsNullOrWhiteSpace(model.RowVersion))
        {
            ModelState.AddModelError("Form.RowVersion", T("Admin.Message.OfferingConcurrencyMissing"));
        }

        var offeringResult = model.ProgramOfferingId > 0
            ? await apiClient.GetProgramOfferingAsync(model.ProgramOfferingId, cancellationToken)
            : null;
        if (offeringResult is null || !offeringResult.IsSuccess || offeringResult.Value is null)
        {
            ModelState.AddModelError(string.Empty, ApiError(offeringResult?.Error, "Admin.Message.OfferingEditNotFound"));
        }

        if (!ModelState.IsValid || offeringResult?.Value is null)
        {
            return await RenderExamRequirementsFormAsync(
                model,
                offeringResult?.Value,
                T("Admin.Message.OfferingValidationFailed"),
                cancellationToken);
        }

        var editForm = CreateOfferingEditForm(offeringResult.Value);
        editForm.RowVersion = model.RowVersion;
        var apiModel = CreateOfferingApiForm(editForm, model.Requirements);
        var result = await apiClient.UpdateProgramOfferingAsync(apiModel, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, ApiError(result.Error, "Admin.Message.OfferingSaveFailed"));
            return await RenderExamRequirementsFormAsync(model, offeringResult.Value, null, cancellationToken);
        }

        TempData["SuccessMessage"] = T("Admin.Message.OfferingSaved");
        return RedirectToAction(nameof(ExamRequirements), new { id = model.ProgramOfferingId });
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
            return RedirectToAction(nameof(OfferingOverview), new { id = programOfferingId });
        }

        var result = await apiClient.CloseInvalidProgramOfferingForRemediationAsync(
            programOfferingId,
            rowVersion,
            cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.RemediationClosed")
            : ApiError(result.Error, "Admin.Message.RemediationFailed");
        return RedirectToAction(nameof(OfferingOverview), new { id = programOfferingId });
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
            return await RenderDocumentRequirementsFormAsync(
                model,
                T("Admin.Message.RequirementValidationFailed"),
                cancellationToken);
        }

        var result = model.PublicId == Guid.Empty
            ? await apiClient.CreateOfferingDocumentRequirementAsync(model, cancellationToken)
            : await apiClient.UpdateOfferingDocumentRequirementAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, ApiError(result.Error, "Admin.Message.RequirementSaveFailed"));
            return await RenderDocumentRequirementsFormAsync(model, null, cancellationToken);
        }

        TempData["SuccessMessage"] = T("Admin.Message.RequirementSaved");
        return RedirectToAction(nameof(DocumentRequirements), new { id = model.ProgramOfferingId });
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
            return RedirectToAction(nameof(DocumentRequirements), new { id = programOfferingId });
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

        return RedirectToAction(nameof(DocumentRequirements), new { id = programOfferingId });
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
    public async Task<IActionResult> AutoFillProgramTranslations(
        string? search,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await apiClient.AutoFillProgramTranslationsAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = ApiError(
                result.Error,
                "Admin.Message.TranslationSaveFailed");
        }
        else
        {
            TempData["SuccessMessage"] = result.Value?.UpdatedCount switch
            {
                0 => T("Admin.Message.TranslationNoChanges"),
                1 => T("Admin.Message.TranslationOneUpdated"),
                var count => UiText.Format(
                    HttpContext,
                    "Admin.Message.TranslationManyUpdated",
                    count)
            };
        }

        return RedirectToAction(
            nameof(ProgramTranslations),
            new
            {
                search,
                page = Math.Max(page, 1),
                pageSize = Math.Clamp(pageSize, 10, 100)
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
            Shell = CreateOfferingShell(evaluation.Value, OfferingSection.Evaluation),
            Evaluation = evaluation.Value,
            Preview = preview.Value ?? new EvaluationRankingPreviewViewModel(),
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
            return await RenderEvaluationCriteriaFormAsync(
                model,
                T("Admin.Message.CriterionInvalid"),
                cancellationToken);
        }

        var result = model.PublicId == Guid.Empty
            ? await apiClient.CreateEvaluationCriterionAsync(model, cancellationToken)
            : await apiClient.UpdateEvaluationCriterionAsync(model, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, ApiError(result.Error, "Admin.Message.CriterionSaveFailed"));
            return await RenderEvaluationCriteriaFormAsync(model, null, cancellationToken);
        }

        TempData["SuccessMessage"] = T("Admin.Message.CriterionSaved");
        return RedirectToAction(nameof(EvaluationCriteria), new { id = model.ProgramOfferingId });
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
            return RedirectToAction(nameof(EvaluationCriteria), new { id = programOfferingId });
        }

        var result = await apiClient.DeleteEvaluationCriterionAsync(
            programOfferingId,
            publicId,
            rowVersion,
            cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? T("Admin.Message.CriterionDeleted")
            : ApiError(result.Error, "Admin.Message.CriterionDeleteFailed");
        return RedirectToAction(nameof(EvaluationCriteria), new { id = programOfferingId });
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
    public IActionResult PublishEvaluation(int id)
    {
        return RedirectToAction(nameof(Results), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Results(int id, CancellationToken cancellationToken)
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

        return View(nameof(PublishEvaluation), new EvaluationPublishPageViewModel
        {
            Shell = CreateOfferingShell(evaluation.Value, OfferingSection.Results),
            ProgramOfferingId = id,
            ProgramName = UiText.SelectLocalized(
                HttpContext,
                evaluation.Value.ProgramName,
                evaluation.Value.ProgramNameEnglish),
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
        return RedirectToAction(nameof(Results), new { id = programOfferingId });
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

    private static OfferingShellViewModel CreateOfferingShell(
        ProgramOfferingAdminViewModel offering,
        OfferingSection activeSection) => new()
        {
            ActiveSection = activeSection,
            Header = new OfferingHeaderViewModel
            {
                ProgramOfferingId = offering.ProgramOfferingId,
                ProgramName = offering.ProgramName,
                ProgramNameEnglish = offering.ProgramNameEnglish,
                DegreeType = offering.DegreeType,
                AcademicYear = offering.AcademicYear,
                Term = offering.Term,
                TermName = offering.TermName,
                IsOpen = offering.IsOpen,
                IsArchived = offering.IsArchived,
                UsesDocumentWorkflow = offering.UsesDocumentWorkflow,
                UsesEvaluationWorkflow = offering.UsesEvaluationWorkflow,
                EvaluationState = offering.EvaluationState
            }
        };

    private static OfferingShellViewModel CreateOfferingShell(
        AdminEvaluationViewModel offering,
        OfferingSection activeSection) => new()
        {
            ActiveSection = activeSection,
            Header = new OfferingHeaderViewModel
            {
                ProgramOfferingId = offering.ProgramOfferingId,
                ProgramName = offering.ProgramName,
                ProgramNameEnglish = offering.ProgramNameEnglish,
                DegreeType = offering.DegreeType,
                AcademicYear = offering.AcademicYear,
                Term = offering.Term,
                TermName = offering.TermName,
                IsOpen = offering.IsOpen,
                UsesDocumentWorkflow = offering.Term != AcademicTerm.LegacyUnspecified,
                UsesEvaluationWorkflow = offering.UsesEvaluationWorkflow,
                EvaluationState = offering.EvaluationState
            }
        };

    private static OfferingEditFormViewModel CreateOfferingEditForm(ProgramOfferingAdminViewModel? offering)
    {
        var nowLocal = IstanbulTime.FromUtc(DateTime.UtcNow);
        return new OfferingEditFormViewModel
        {
            ProgramOfferingId = offering?.ProgramOfferingId ?? 0,
            ProgramId = offering?.ProgramId ?? 0,
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
            RowVersion = offering?.RowVersion
        };
    }

    private static ProgramOfferingFormViewModel CreateOfferingApiForm(
        OfferingEditFormViewModel form,
        IEnumerable<ExamRequirementViewModel> requirements) =>
        CreateOfferingApiForm(
            form,
            requirements.Select(item => new ProgramOfferingRequirementInputViewModel
            {
                IsConfigured = true,
                ExamId = item.ExamId,
                MinimumScore = item.MinimumScore,
                MinimumValidityDate = item.MinimumValidityDate,
                IsRequired = item.IsRequired
            }));

    private static ProgramOfferingFormViewModel CreateOfferingApiForm(
        OfferingEditFormViewModel form,
        IEnumerable<ProgramOfferingRequirementInputViewModel> requirements) => new()
        {
            ProgramOfferingId = form.ProgramOfferingId,
            ProgramId = form.ProgramId,
            AcademicYearStart = form.AcademicYearStart,
            Term = form.Term,
            ApplicationStartLocal = form.ApplicationStartLocal,
            ApplicationDeadlineLocal = form.ApplicationDeadlineLocal,
            Quota = form.Quota,
            IsOpen = form.IsOpen,
            IsArchived = form.IsArchived,
            UsesEvaluationWorkflow = form.UsesEvaluationWorkflow,
            RowVersion = form.RowVersion,
            ExamRequirements = requirements.ToList()
        };

    private static OfferingExamRequirementsPageViewModel CreateExamRequirementsPage(
        ProgramOfferingAdminViewModel offering,
        ProgramOfferingCatalogViewModel catalog,
        OfferingExamRequirementsFormViewModel? postedForm,
        string? errorMessage)
    {
        var posted = postedForm?.Requirements
            .GroupBy(item => item.ExamId)
            .ToDictionary(group => group.Key, group => group.First());
        var existing = offering.ExamRequirements.ToDictionary(item => item.ExamId);
        var requirements = catalog.Exams.Select(exam =>
        {
            if (posted is not null && posted.TryGetValue(exam.ExamId, out var postedRequirement))
            {
                return postedRequirement;
            }

            existing.TryGetValue(exam.ExamId, out var current);
            return new ProgramOfferingRequirementInputViewModel
            {
                IsConfigured = current is not null,
                ExamId = exam.ExamId,
                MinimumScore = current?.MinimumScore ?? 0,
                MinimumValidityDate = current?.MinimumValidityDate,
                IsRequired = current?.IsRequired ?? false
            };
        }).ToList();
        return new OfferingExamRequirementsPageViewModel
        {
            Shell = CreateOfferingShell(offering, OfferingSection.ExamRequirements),
            Form = new OfferingExamRequirementsFormViewModel
            {
                ProgramOfferingId = offering.ProgramOfferingId,
                RowVersion = postedForm?.RowVersion ?? offering.RowVersion,
                Requirements = requirements
            },
            Exams = catalog.Exams,
            CanEdit = offering.DraftApplicationCount + offering.SubmittedOrLaterApplicationCount == 0
                && (!offering.UsesEvaluationWorkflow
                    || offering.EvaluationState == OfferingEvaluationState.Configuring),
            ErrorMessage = errorMessage
        };
    }

    private static OfferingDocumentRequirementsPageViewModel CreateDocumentRequirementsPage(
        ProgramOfferingAdminViewModel offering,
        IReadOnlyList<OfferingDocumentRequirementViewModel> requirements,
        Guid? editRequirementId,
        OfferingDocumentRequirementFormViewModel? postedForm,
        string? errorMessage)
    {
        var canEdit = !offering.UsesEvaluationWorkflow
            || offering.EvaluationState == OfferingEvaluationState.Configuring;
        var selected = canEdit && editRequirementId.HasValue
            ? requirements.SingleOrDefault(item => item.PublicId == editRequirementId.Value)
            : null;
        return new OfferingDocumentRequirementsPageViewModel
        {
            Shell = CreateOfferingShell(offering, OfferingSection.DocumentRequirements),
            Requirements = requirements,
            CanEdit = canEdit,
            DocumentRequirementForm = postedForm ?? new OfferingDocumentRequirementFormViewModel
            {
                ProgramOfferingId = canEdit ? offering.ProgramOfferingId : 0,
                PublicId = selected?.PublicId ?? Guid.Empty,
                DocumentCode = selected?.DocumentCode ?? string.Empty,
                DisplayName = selected?.DisplayName ?? string.Empty,
                Description = selected?.Description,
                IsRequired = selected?.IsRequired ?? true,
                AllowedContentCategory = selected?.AllowedContentCategory ?? DocumentContentCategory.PdfOrImage,
                MaximumBytes = selected?.MaximumBytes ?? 10485760,
                RowVersion = selected?.RowVersion
            },
            ErrorMessage = errorMessage
        };
    }

    private async Task<IActionResult> RenderOfferingEditFormAsync(
        OfferingEditFormViewModel form,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var catalogTask = apiClient.GetProgramOfferingCatalogAsync(cancellationToken);
        var offeringTask = form.ProgramOfferingId > 0
            ? apiClient.GetProgramOfferingAsync(form.ProgramOfferingId, cancellationToken)
            : null;
        if (offeringTask is not null)
        {
            await Task.WhenAll(catalogTask, offeringTask);
        }

        var catalog = await catalogTask;
        var offering = offeringTask is null ? null : await offeringTask;
        return View(nameof(EditOffering), new OfferingEditPageViewModel
        {
            Shell = offering?.Value is null
                ? null
                : CreateOfferingShell(offering.Value, OfferingSection.Edit),
            Form = form,
            Programs = catalog.Value?.Programs ?? [],
            HasActiveRequiredDocumentRequirement = offering?.Value?.HasActiveRequiredDocumentRequirement ?? false,
            ErrorMessage = errorMessage
                ?? ApiError(offering?.Error ?? catalog.Error, "Admin.Message.OfferingsLoadFailed")
        });
    }

    private async Task<IActionResult> RenderExamRequirementsFormAsync(
        OfferingExamRequirementsFormViewModel form,
        ProgramOfferingAdminViewModel? offering,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var offeringResult = offering is null
            ? await apiClient.GetProgramOfferingAsync(form.ProgramOfferingId, cancellationToken)
            : null;
        offering ??= offeringResult?.Value;
        if (offering is null)
        {
            TempData["ErrorMessage"] = ApiError(offeringResult?.Error, "Admin.Message.OfferingEditNotFound");
            return RedirectToAction(nameof(Offerings));
        }

        var catalog = await apiClient.GetProgramOfferingCatalogAsync(cancellationToken);
        return View(
            nameof(ExamRequirements),
            CreateExamRequirementsPage(
                offering,
                catalog.Value ?? new ProgramOfferingCatalogViewModel(),
                form,
                errorMessage ?? ApiError(catalog.Error, "Admin.Message.OfferingsLoadFailed")));
    }

    private async Task<IActionResult> RenderDocumentRequirementsFormAsync(
        OfferingDocumentRequirementFormViewModel form,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var offeringTask = apiClient.GetProgramOfferingAsync(form.ProgramOfferingId, cancellationToken);
        var requirementsTask = apiClient.GetOfferingDocumentRequirementsAsync(form.ProgramOfferingId, cancellationToken);
        await Task.WhenAll(offeringTask, requirementsTask);
        var offering = await offeringTask;
        var requirements = await requirementsTask;
        if (offering.Value is null)
        {
            TempData["ErrorMessage"] = ApiError(offering.Error, "Admin.Message.RequirementOfferingNotFound");
            return RedirectToAction(nameof(Offerings));
        }

        return View(
            nameof(DocumentRequirements),
            CreateDocumentRequirementsPage(
                offering.Value,
                requirements.Value ?? [],
                null,
                form,
                errorMessage ?? ApiError(requirements.Error, "Admin.Message.RequirementsLoadFailed")));
    }

    private async Task<IActionResult> RenderEvaluationCriteriaFormAsync(
        EvaluationCriterionFormViewModel form,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var offeringTask = apiClient.GetProgramOfferingAsync(form.ProgramOfferingId, cancellationToken);
        var criteriaTask = apiClient.GetEvaluationCriteriaAsync(form.ProgramOfferingId, cancellationToken);
        await Task.WhenAll(offeringTask, criteriaTask);
        var offering = await offeringTask;
        var criteria = await criteriaTask;
        if (offering.Value is null)
        {
            TempData["ErrorMessage"] = ApiError(offering.Error, "Admin.Message.EvaluationNotFound");
            return RedirectToAction(nameof(Offerings));
        }

        return View(nameof(EvaluationCriteria), new EvaluationCriteriaPageViewModel
        {
            Shell = CreateOfferingShell(offering.Value, OfferingSection.EvaluationCriteria),
            Criteria = criteria.Value ?? [],
            EligibleExamRequirements = offering.Value.ExamRequirements.Where(item => item.IsRequired).ToArray(),
            CriterionForm = form,
            CanEdit = offering.Value.EvaluationState == OfferingEvaluationState.Configuring
                && !offering.Value.IsOpen
                && offering.Value.DraftApplicationCount + offering.Value.SubmittedOrLaterApplicationCount == 0,
            ErrorMessage = errorMessage ?? ApiError(criteria.Error, "Admin.Message.EvaluationNotFound")
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
