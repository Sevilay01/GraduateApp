using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GraduateApp.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Web.Services;

public sealed class GraduateApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public Task<ApiResult<LoginApiResponse>> LoginAsync(LoginViewModel model, CancellationToken cancellationToken) =>
        PostAsync<LoginApiResponse>(
            "api/auth/login",
            new { model.Username, model.Password, model.AccountType },
            cancellationToken);

    public Task<ApiResult> RegisterAsync(RegisterViewModel model, CancellationToken cancellationToken) =>
        PostAsync("api/auth/register", new
        {
            model.Tc,
            model.FirstName,
            model.LastName,
            model.FatherName,
            model.BirthDate,
            model.Email,
            model.Telephone,
            model.Password,
            model.ConfirmPassword
        }, cancellationToken);

    public Task<ApiResult> ForgotPasswordAsync(ForgotPasswordViewModel model, CancellationToken cancellationToken) =>
        PostAsync("api/auth/forgot-password", new { model.Email }, cancellationToken);

    public Task<ApiResult<PasswordResetApiResponse>> ResetPasswordAsync(
        ResetPasswordViewModel model,
        CancellationToken cancellationToken) =>
        PostAsync<PasswordResetApiResponse>("api/auth/reset-password", new
        {
            model.Token,
            model.NewPassword,
            model.ConfirmPassword
        }, cancellationToken);

    public Task<ApiResult> AcceptAdminInvitationAsync(
        AcceptAdminInvitationViewModel model,
        CancellationToken cancellationToken) =>
        PostAsync("api/auth/admin-invitations/accept", new
        {
            model.Token,
            model.NewPassword,
            model.ConfirmPassword
        }, cancellationToken);

    public Task<ApiResult> ChangePasswordAsync(ChangePasswordViewModel model, CancellationToken cancellationToken) =>
        PostAsync("api/auth/change-password", new
        {
            model.CurrentPassword,
            model.NewPassword,
            model.ConfirmPassword
        }, cancellationToken);

    public Task<ApiResult> LogoutAsync(CancellationToken cancellationToken) =>
        PostAsync("api/auth/logout", new { }, cancellationToken);

    public Task<ApiResult<IReadOnlyList<ProgramViewModel>>> GetOpenProgramsAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<ProgramViewModel>>("api/programs/open", cancellationToken);

    public Task<ApiResult<IReadOnlyList<PanelApplicationViewModel>>> GetMyApplicationsAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<PanelApplicationViewModel>>("api/applications/mine", cancellationToken);

    public Task<ApiResult<PanelApplicationViewModel>> CreateApplicationAsync(int programOfferingId, CancellationToken cancellationToken) =>
        PostAsync<PanelApplicationViewModel>("api/applications", new { programOfferingId }, cancellationToken);

    public Task<ApiResult<StudentApplicationDetailViewModel>> GetMyApplicationAsync(
        Guid publicId,
        CancellationToken cancellationToken) =>
        GetAsync<StudentApplicationDetailViewModel>($"api/applications/mine/{publicId:D}", cancellationToken);

    public Task<ApiResult<PublishedApplicationEvaluationViewModel>> GetMyPublishedEvaluationAsync(
        Guid publicId,
        CancellationToken cancellationToken) =>
        GetAsync<PublishedApplicationEvaluationViewModel>(
            $"api/applications/mine/{publicId:D}/evaluation-result",
            cancellationToken);

    public Task<ApiResult> SubmitApplicationAsync(Guid publicId, CancellationToken cancellationToken) =>
        PostAsync($"api/applications/mine/{publicId:D}/submit", new { }, cancellationToken);

    public async Task<ApiResult<ApplicationDocumentViewModel>> UploadApplicationDocumentAsync(
        Guid applicationPublicId,
        Guid requirementPublicId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/applications/mine/{applicationPublicId:D}/document-requirements/{requirementPublicId:D}/upload");
        using var multipart = new MultipartFormDataContent();
        await using var input = file.OpenReadStream();
        using var fileContent = new StreamContent(input);
        if (System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(file.ContentType, out var contentType))
        {
            fileContent.Headers.ContentType = contentType;
        }

        multipart.Add(fileContent, "file", Path.GetFileName(file.FileName));
        request.Content = multipart;
        return await SendAsync<ApplicationDocumentViewModel>(request, cancellationToken);
    }

    public Task<ApiDownloadResult> DownloadMyDocumentAsync(
        Guid applicationPublicId,
        Guid documentPublicId,
        CancellationToken cancellationToken) =>
        DownloadAsync(
            $"api/applications/mine/{applicationPublicId:D}/documents/{documentPublicId:D}/download",
            cancellationToken);

    public Task<ApiResult<IReadOnlyList<StudentExamScoreViewModel>>> GetMyExamScoresAsync(
        CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<StudentExamScoreViewModel>>("api/students/me/exam-scores", cancellationToken);

    public Task<ApiResult<IReadOnlyList<StudentExamCatalogItemViewModel>>> GetExamCatalogAsync(
        CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<StudentExamCatalogItemViewModel>>("api/students/me/exam-scores/catalog", cancellationToken);

    public Task<ApiResult<StudentExamScoreViewModel>> CreateExamScoreAsync(
        StudentExamScoreInputViewModel model,
        CancellationToken cancellationToken) =>
        PostAsync<StudentExamScoreViewModel>("api/students/me/exam-scores", new
        {
            model.ExamId,
            model.Score,
            model.ExamDate
        }, cancellationToken);

    public Task<ApiResult<StudentExamScoreViewModel>> UpdateExamScoreAsync(
        StudentExamScoreInputViewModel model,
        CancellationToken cancellationToken) =>
        PutAsync<StudentExamScoreViewModel>($"api/students/me/exam-scores/{model.ScoreId}", new
        {
            model.ExamId,
            model.Score,
            model.ExamDate
        }, cancellationToken);

    public Task<ApiResult> DeleteExamScoreAsync(int scoreId, CancellationToken cancellationToken) =>
        DeleteAsync($"api/students/me/exam-scores/{scoreId}", cancellationToken);

    public Task<ApiResult<PagedResultViewModel<AdminApplicationListItemViewModel>>> GetAdminApplicationsAsync(
        string? search,
        ApplicationStatus? status,
        int? academicYearStart,
        AcademicTerm? term,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"page={Math.Max(page, 1)}",
            $"pageSize={Math.Clamp(pageSize, 10, 100)}"
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        }

        if (status.HasValue)
        {
            query.Add($"status={status.Value}");
        }

        if (academicYearStart.HasValue)
        {
            query.Add($"academicYearStart={academicYearStart.Value}");
        }

        if (term.HasValue)
        {
            query.Add($"term={term.Value}");
        }

        return GetAsync<PagedResultViewModel<AdminApplicationListItemViewModel>>(
            $"api/applications/admin?{string.Join('&', query)}",
            cancellationToken);
    }

    public Task<ApiResult<IReadOnlyList<ProgramOfferingAdminViewModel>>> GetProgramOfferingsAsync(
        int? academicYearStart,
        AcademicTerm? term,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var query = new List<string> { $"includeArchived={includeArchived.ToString().ToLowerInvariant()}" };
        if (academicYearStart.HasValue)
        {
            query.Add($"academicYearStart={academicYearStart.Value}");
        }

        if (term.HasValue)
        {
            query.Add($"term={term.Value}");
        }

        return GetAsync<IReadOnlyList<ProgramOfferingAdminViewModel>>(
            $"api/program-offerings?{string.Join('&', query)}",
            cancellationToken);
    }

    public Task<ApiResult<ProgramOfferingCatalogViewModel>> GetProgramOfferingCatalogAsync(CancellationToken cancellationToken) =>
        GetAsync<ProgramOfferingCatalogViewModel>("api/program-offerings/catalog", cancellationToken);

    public Task<ApiResult<ProgramOfferingAdminViewModel>> CreateProgramOfferingAsync(
        ProgramOfferingFormViewModel model,
        CancellationToken cancellationToken) =>
        PostAsync<ProgramOfferingAdminViewModel>("api/program-offerings", MapOfferingRequest(model, includeConcurrency: false), cancellationToken);

    public Task<ApiResult<ProgramOfferingAdminViewModel>> UpdateProgramOfferingAsync(
        ProgramOfferingFormViewModel model,
        CancellationToken cancellationToken) =>
        PutAsync<ProgramOfferingAdminViewModel>(
            $"api/program-offerings/{model.ProgramOfferingId}",
            MapOfferingRequest(model, includeConcurrency: true),
            cancellationToken);

    public Task<ApiResult<IReadOnlyList<OfferingDocumentRequirementViewModel>>> GetOfferingDocumentRequirementsAsync(
        int offeringId,
        CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<OfferingDocumentRequirementViewModel>>(
            $"api/program-offerings/{offeringId}/document-requirements",
            cancellationToken);

    public Task<ApiResult<OfferingDocumentRequirementViewModel>> CreateOfferingDocumentRequirementAsync(
        OfferingDocumentRequirementFormViewModel model,
        CancellationToken cancellationToken) =>
        PostAsync<OfferingDocumentRequirementViewModel>(
            $"api/program-offerings/{model.ProgramOfferingId}/document-requirements",
            MapDocumentRequirement(model, includeConcurrency: false),
            cancellationToken);

    public Task<ApiResult<OfferingDocumentRequirementViewModel>> UpdateOfferingDocumentRequirementAsync(
        OfferingDocumentRequirementFormViewModel model,
        CancellationToken cancellationToken) =>
        PutAsync<OfferingDocumentRequirementViewModel>(
            $"api/program-offerings/{model.ProgramOfferingId}/document-requirements/{model.PublicId:D}",
            MapDocumentRequirement(model, includeConcurrency: true),
            cancellationToken);

    public Task<ApiResult<OfferingDocumentRequirementViewModel>> SetOfferingDocumentRequirementActiveAsync(
        int offeringId,
        Guid publicId,
        bool isActive,
        string rowVersion,
        CancellationToken cancellationToken) =>
        PostAsync<OfferingDocumentRequirementViewModel>(
            $"api/program-offerings/{offeringId}/document-requirements/{publicId:D}/active",
            new { isActive, rowVersion },
            cancellationToken);

    public Task<ApiResult<AdminEvaluationViewModel>> GetEvaluationAsync(
        int offeringId,
        CancellationToken cancellationToken) =>
        GetAsync<AdminEvaluationViewModel>($"api/evaluations/offerings/{offeringId}", cancellationToken);

    public Task<ApiResult<EvaluationRankingPreviewViewModel>> GetEvaluationPreviewAsync(
        int offeringId,
        CancellationToken cancellationToken) =>
        GetAsync<EvaluationRankingPreviewViewModel>(
            $"api/evaluations/offerings/{offeringId}/preview",
            cancellationToken);

    public Task<ApiResult<EvaluationCriterionViewModel>> CreateEvaluationCriterionAsync(
        EvaluationCriterionFormViewModel model,
        CancellationToken cancellationToken) =>
        PostAsync<EvaluationCriterionViewModel>(
            $"api/program-offerings/{model.ProgramOfferingId}/evaluation-criteria",
            MapEvaluationCriterion(model, includeConcurrency: false),
            cancellationToken);

    public Task<ApiResult<EvaluationCriterionViewModel>> UpdateEvaluationCriterionAsync(
        EvaluationCriterionFormViewModel model,
        CancellationToken cancellationToken) =>
        PutAsync<EvaluationCriterionViewModel>(
            $"api/program-offerings/{model.ProgramOfferingId}/evaluation-criteria/{model.PublicId:D}",
            MapEvaluationCriterion(model, includeConcurrency: true),
            cancellationToken);

    public Task<ApiResult> DeleteEvaluationCriterionAsync(
        int offeringId,
        Guid publicId,
        string rowVersion,
        CancellationToken cancellationToken) =>
        DeleteAsync(
            $"api/program-offerings/{offeringId}/evaluation-criteria/{publicId:D}",
            new { rowVersion },
            cancellationToken);

    public Task<ApiResult> DecideEvaluationEligibilityAsync(
        Guid applicationPublicId,
        EvaluationEligibilityStatus eligibilityStatus,
        string? ineligibilityReason,
        string rowVersion,
        CancellationToken cancellationToken) =>
        PostAsync($"api/evaluations/applications/{applicationPublicId:D}/eligibility", new
        {
            eligibilityStatus,
            ineligibilityReason,
            rowVersion
        }, cancellationToken);

    public Task<ApiResult> SetManualEvaluationScoreAsync(
        Guid applicationPublicId,
        Guid criterionPublicId,
        decimal rawScore,
        string rowVersion,
        CancellationToken cancellationToken) =>
        PostAsync(
            $"api/evaluations/applications/{applicationPublicId:D}/criteria/{criterionPublicId:D}/score",
            new { rawScore, rowVersion },
            cancellationToken);

    public Task<ApiResult> FinalizeEvaluationAsync(
        int offeringId,
        string rowVersion,
        CancellationToken cancellationToken) =>
        PostAsync($"api/evaluations/offerings/{offeringId}/finalize", new { rowVersion }, cancellationToken);

    public Task<ApiResult<EvaluationPublicationSummaryViewModel>> GetEvaluationPublicationSummaryAsync(
        int offeringId,
        CancellationToken cancellationToken) =>
        GetAsync<EvaluationPublicationSummaryViewModel>(
            $"api/evaluations/offerings/{offeringId}/publication-summary",
            cancellationToken);

    public Task<ApiResult> PublishEvaluationAsync(
        int offeringId,
        string rowVersion,
        CancellationToken cancellationToken) =>
        PostAsync($"api/evaluations/offerings/{offeringId}/publish", new { rowVersion }, cancellationToken);

    public Task<ApiResult<AdminApplicationDetailViewModel>> GetAdminApplicationDetailAsync(
        Guid applicationPublicId,
        CancellationToken cancellationToken) =>
        GetAsync<AdminApplicationDetailViewModel>($"api/applications/admin/{applicationPublicId:D}", cancellationToken);

    public Task<ApiResult<PagedResultViewModel<AdminStudentListItemViewModel>>> GetAdminStudentsAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"page={Math.Max(page, 1)}",
            $"pageSize={Math.Clamp(pageSize, 10, 100)}"
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        }

        return GetAsync<PagedResultViewModel<AdminStudentListItemViewModel>>(
            $"api/admin/students?{string.Join('&', query)}",
            cancellationToken);
    }

    public Task<ApiResult<AdminStudentDetailViewModel>> GetAdminStudentAsync(
        Guid publicId,
        CancellationToken cancellationToken) =>
        GetAsync<AdminStudentDetailViewModel>(
            $"api/admin/students/{publicId:D}",
            cancellationToken);

    public Task<ApiResult> DeactivateStudentAsync(Guid publicId, CancellationToken cancellationToken) =>
        PostAsync($"api/admin/students/{publicId:D}/deactivate", new { }, cancellationToken);

    public Task<ApiResult> ActivateStudentAsync(Guid publicId, CancellationToken cancellationToken) =>
        PostAsync($"api/admin/students/{publicId:D}/activate", new { }, cancellationToken);

    public Task<ApiResult<PagedResultViewModel<AdminAccountViewModel>>> GetAdminAccountsAsync(
        string? search,
        AdminAccountStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"page={Math.Max(page, 1)}",
            $"pageSize={Math.Clamp(pageSize, 10, 100)}"
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        }

        if (status.HasValue)
        {
            query.Add($"status={status.Value}");
        }

        return GetAsync<PagedResultViewModel<AdminAccountViewModel>>(
            $"api/admin/accounts?{string.Join('&', query)}",
            cancellationToken);
    }

    public Task<ApiResult<AdminAccountViewModel>> InviteAdminAsync(
        InviteAdminViewModel model,
        CancellationToken cancellationToken) =>
        PostAsync<AdminAccountViewModel>("api/admin/accounts/invitations", new { model.Email }, cancellationToken);

    public Task<ApiResult<AdminAccountViewModel>> ResendAdminInvitationAsync(Guid publicId, string rowVersion, CancellationToken cancellationToken) =>
        PostAsync<AdminAccountViewModel>($"api/admin/accounts/{publicId:D}/resend-invitation", new { rowVersion }, cancellationToken);

    public Task<ApiResult<AdminAccountViewModel>> ActivateAdminAsync(Guid publicId, string rowVersion, CancellationToken cancellationToken) =>
        PostAsync<AdminAccountViewModel>($"api/admin/accounts/{publicId:D}/activate", new { rowVersion }, cancellationToken);

    public Task<ApiResult<AdminAccountViewModel>> DeactivateAdminAsync(Guid publicId, string rowVersion, CancellationToken cancellationToken) =>
        PostAsync<AdminAccountViewModel>($"api/admin/accounts/{publicId:D}/deactivate", new { rowVersion }, cancellationToken);

    public Task<ApiResult<AdminAccountViewModel>> UnlockAdminAsync(Guid publicId, string rowVersion, CancellationToken cancellationToken) =>
        PostAsync<AdminAccountViewModel>($"api/admin/accounts/{publicId:D}/unlock", new { rowVersion }, cancellationToken);

    public Task<ApiResult<PagedResultViewModel<InstituteAdminViewModel>>> GetAdminInstitutesAsync(
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"page={Math.Max(page, 1)}",
            $"pageSize={Math.Clamp(pageSize, 10, 100)}"
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        }

        if (isActive.HasValue)
        {
            query.Add($"isActive={isActive.Value.ToString().ToLowerInvariant()}");
        }

        return GetAsync<PagedResultViewModel<InstituteAdminViewModel>>(
            $"api/admin/institutes?{string.Join('&', query)}",
            cancellationToken);
    }

    public Task<ApiResult<InstituteAdminViewModel>> GetAdminInstituteAsync(
        int instituteId,
        CancellationToken cancellationToken) =>
        GetAsync<InstituteAdminViewModel>($"api/admin/institutes/{instituteId}", cancellationToken);

    public Task<ApiResult<InstituteAdminViewModel>> CreateInstituteAsync(
        InstituteFormViewModel model,
        CancellationToken cancellationToken) =>
        PostAsync<InstituteAdminViewModel>("api/admin/institutes", new { model.InstituteName }, cancellationToken);

    public Task<ApiResult<InstituteAdminViewModel>> UpdateInstituteAsync(
        InstituteFormViewModel model,
        CancellationToken cancellationToken) =>
        PutAsync<InstituteAdminViewModel>(
            $"api/admin/institutes/{model.InstituteId}",
            new { model.InstituteName, model.RowVersion },
            cancellationToken);

    public Task<ApiResult<InstituteAdminViewModel>> ActivateInstituteAsync(
        int instituteId,
        string rowVersion,
        CancellationToken cancellationToken) =>
        PostAsync<InstituteAdminViewModel>(
            $"api/admin/institutes/{instituteId}/activate",
            new { rowVersion },
            cancellationToken);

    public Task<ApiResult<InstituteAdminViewModel>> DeactivateInstituteAsync(
        int instituteId,
        string rowVersion,
        CancellationToken cancellationToken) =>
        PostAsync<InstituteAdminViewModel>(
            $"api/admin/institutes/{instituteId}/deactivate",
            new { rowVersion },
            cancellationToken);

    public Task<ApiResult> DeleteInstituteAsync(
        int instituteId,
        string rowVersion,
        CancellationToken cancellationToken) =>
        DeleteAsync(
            $"api/admin/institutes/{instituteId}?rowVersion={Uri.EscapeDataString(rowVersion)}",
            cancellationToken);

    public Task<ApiResult<PagedResultViewModel<ProgramAdminViewModel>>> GetAdminProgramsAsync(
        string? search,
        bool? isActive,
        int? instituteId,
        string? degreeType,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"page={Math.Max(page, 1)}",
            $"pageSize={Math.Clamp(pageSize, 10, 100)}"
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        }

        if (isActive.HasValue)
        {
            query.Add($"isActive={isActive.Value.ToString().ToLowerInvariant()}");
        }

        if (instituteId.HasValue)
        {
            query.Add($"instituteId={instituteId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(degreeType))
        {
            query.Add($"degreeType={Uri.EscapeDataString(degreeType)}");
        }

        return GetAsync<PagedResultViewModel<ProgramAdminViewModel>>(
            $"api/admin/programs?{string.Join('&', query)}",
            cancellationToken);
    }

    public Task<ApiResult<ProgramAdminViewModel>> GetAdminProgramAsync(
        int programId,
        CancellationToken cancellationToken) =>
        GetAsync<ProgramAdminViewModel>($"api/admin/programs/{programId}", cancellationToken);

    public Task<ApiResult<ProgramAdminViewModel>> CreateProgramAsync(
        ProgramFormViewModel model,
        CancellationToken cancellationToken) =>
        PostAsync<ProgramAdminViewModel>("api/admin/programs", new
        {
            model.InstituteId,
            model.ProgramName,
            model.DegreeType
        }, cancellationToken);

    public Task<ApiResult<ProgramAdminViewModel>> UpdateProgramAsync(
        ProgramFormViewModel model,
        CancellationToken cancellationToken) =>
        PutAsync<ProgramAdminViewModel>($"api/admin/programs/{model.ProgramId}", new
        {
            model.InstituteId,
            model.ProgramName,
            model.DegreeType,
            model.RowVersion
        }, cancellationToken);

    public Task<ApiResult<ProgramAdminViewModel>> ActivateProgramAsync(
        int programId,
        string rowVersion,
        CancellationToken cancellationToken) =>
        PostAsync<ProgramAdminViewModel>(
            $"api/admin/programs/{programId}/activate",
            new { rowVersion },
            cancellationToken);

    public Task<ApiResult<ProgramAdminViewModel>> DeactivateProgramAsync(
        int programId,
        string rowVersion,
        CancellationToken cancellationToken) =>
        PostAsync<ProgramAdminViewModel>(
            $"api/admin/programs/{programId}/deactivate",
            new { rowVersion },
            cancellationToken);

    public Task<ApiResult> DeleteProgramAsync(
        int programId,
        string rowVersion,
        CancellationToken cancellationToken) =>
        DeleteAsync(
            $"api/admin/programs/{programId}?rowVersion={Uri.EscapeDataString(rowVersion)}",
            cancellationToken);

    public Task<ApiResult> UpdateApplicationStatusAsync(
        UpdateApplicationStatusViewModel model,
        CancellationToken cancellationToken) =>
        PostAsync($"api/applications/admin/{model.PublicId:D}/status", new
        {
            model.NewStatus,
            model.RowVersion,
            model.Notes
        }, cancellationToken);

    public Task<ApiResult<ApplicationDocumentViewModel>> ReviewApplicationDocumentAsync(
        ReviewApplicationDocumentViewModel model,
        CancellationToken cancellationToken) =>
        PostAsync<ApplicationDocumentViewModel>(
            $"api/applications/admin/{model.ApplicationPublicId:D}/documents/{model.DocumentPublicId:D}/review",
            new { model.ReviewStatus, model.RejectionReason, model.RowVersion },
            cancellationToken);

    public Task<ApiDownloadResult> DownloadAdminDocumentAsync(
        Guid applicationPublicId,
        Guid documentPublicId,
        CancellationToken cancellationToken) =>
        DownloadAsync(
            $"api/applications/admin/{applicationPublicId:D}/documents/{documentPublicId:D}/download",
            cancellationToken);

    public Task<ApiResult<StudentProfileApiModel>> GetProfileAsync(CancellationToken cancellationToken) =>
        GetAsync<StudentProfileApiModel>("api/students/me", cancellationToken);

    public Task<ApiResult<IReadOnlyList<UniversityViewModel>>> GetUniversitiesAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<UniversityViewModel>>("api/students/universities", cancellationToken);

    public Task<ApiResult<StudentProfileApiModel>> UpdateProfileAsync(
        StudentProfileViewModel model,
        CancellationToken cancellationToken) =>
        PutAsync<StudentProfileApiModel>("api/students/me", new
        {
            model.FirstName,
            model.LastName,
            model.Email,
            model.Telephone,
            model.FatherName,
            model.BirthDate,
            Education = model.UniversityId.HasValue && model.UniversityId > 0
                ? new
                {
                    UniversityId = model.UniversityId.Value,
                    model.Faculty,
                    model.GraduatedProgram,
                    model.Gno
                }
                : null
        }, cancellationToken);

    private async Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<ApiDownloadResult> DownloadAsync(string path, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            request.Dispose();
            return ApiDownloadResult.Failure("Servis yanıt vermedi. Lütfen tekrar deneyin.");
        }
        catch (HttpRequestException)
        {
            request.Dispose();
            return ApiDownloadResult.Failure("Servise şu anda ulaşılamıyor. Lütfen daha sonra tekrar deneyin.");
        }

        request.Dispose();
        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                return ApiDownloadResult.Failure(await ReadErrorAsync(response, cancellationToken), response.StatusCode);
            }
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var suppliedName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? "belge";
        var safeName = Path.GetFileName(suppliedName.Trim('"').Replace('\\', '/'));
        return ApiDownloadResult.Success(
            new ResponseOwnedStream(stream, response),
            response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            string.IsNullOrWhiteSpace(safeName) ? "belge" : safeName,
            response.StatusCode);
    }

    private async Task<ApiResult<T>> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<ApiResult> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        return await SendAsync(request, cancellationToken);
    }

    private async Task<ApiResult<T>> PutAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<ApiResult> PutAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        return await SendAsync(request, cancellationToken);
    }

    private async Task<ApiResult> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        return await SendAsync(request, cancellationToken);
    }

    private async Task<ApiResult> DeleteAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        return await SendAsync(request, cancellationToken);
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApiResult<T>.Failure("Servis yanıt vermedi. Lütfen tekrar deneyin.");
        }
        catch (HttpRequestException)
        {
            return ApiResult<T>.Failure("Servise şu anda ulaşılamıyor. Lütfen daha sonra tekrar deneyin.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return ApiResult<T>.Failure(await ReadErrorAsync(response, cancellationToken), response.StatusCode);
            }

            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                return value is null
                    ? ApiResult<T>.Failure("Servisten geçersiz bir yanıt alındı.", response.StatusCode)
                    : ApiResult<T>.Success(value, response.StatusCode);
            }
            catch (JsonException)
            {
                return ApiResult<T>.Failure("Servisten geçersiz bir yanıt alındı.", response.StatusCode);
            }
        }
    }

    private async Task<ApiResult> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApiResult.Failure("Servis yanıt vermedi. Lütfen tekrar deneyin.");
        }
        catch (HttpRequestException)
        {
            return ApiResult.Failure("Servise şu anda ulaşılamıyor. Lütfen daha sonra tekrar deneyin.");
        }

        using (response)
        {
            return response.IsSuccessStatusCode
                ? ApiResult.Success(response.StatusCode)
                : ApiResult.Failure(await ReadErrorAsync(response, cancellationToken), response.StatusCode);
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(problem?.Detail) && problem.Detail.Length <= 500)
            {
                return problem.Detail;
            }
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // Failure payloads are optional; status-based messages remain deterministic.
        }

        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => "Gönderilen bilgiler doğrulanamadı.",
            HttpStatusCode.Unauthorized => "Oturum doğrulanamadı. Lütfen tekrar giriş yapın.",
            HttpStatusCode.Forbidden => "Bu işlem için yetkiniz yok.",
            HttpStatusCode.NotFound => "İstenen kayıt bulunamadı.",
            HttpStatusCode.Conflict => "İşlem mevcut kayıtla çakıştı. Sayfayı yenileyip tekrar deneyin.",
            (HttpStatusCode)429 => "Çok fazla istek gönderildi. Lütfen bir süre bekleyin.",
            _ => "İşlem tamamlanamadı. Lütfen daha sonra tekrar deneyin."
        };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static object MapOfferingRequest(ProgramOfferingFormViewModel model, bool includeConcurrency) => new
    {
        model.ProgramId,
        model.AcademicYearStart,
        model.Term,
        ApplicationStartUtc = IstanbulTime.ToUtc(model.ApplicationStartLocal),
        ApplicationDeadlineUtc = IstanbulTime.ToUtc(model.ApplicationDeadlineLocal),
        model.Quota,
        model.IsOpen,
        model.IsArchived,
        model.UsesEvaluationWorkflow,
        RowVersion = includeConcurrency ? model.RowVersion : null,
        ExamRequirements = model.ExamRequirements
            .Where(requirement => requirement.IsConfigured)
            .Select(requirement => new
            {
                requirement.ExamId,
                requirement.MinimumScore,
                requirement.MinimumValidityDate,
                requirement.IsRequired
            })
    };

    private static object MapDocumentRequirement(
        OfferingDocumentRequirementFormViewModel model,
        bool includeConcurrency) => new
        {
            model.DocumentCode,
            model.DisplayName,
            model.Description,
            model.IsRequired,
            model.AllowedContentCategory,
            model.MaximumBytes,
            RowVersion = includeConcurrency ? model.RowVersion : null
        };

    private static object MapEvaluationCriterion(
        EvaluationCriterionFormViewModel model,
        bool includeConcurrency) => new
        {
            model.Code,
            model.DisplayName,
            model.SourceType,
            model.ExamId,
            model.WeightBasisPoints,
            model.MaximumRawScore,
            model.TieBreakPriority,
            RowVersion = includeConcurrency ? model.RowVersion : null
        };
}
