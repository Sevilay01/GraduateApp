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

    public Task<ApiResult> CreateApplicationAsync(int programOfferingId, CancellationToken cancellationToken) =>
        PostAsync("api/applications", new { programOfferingId }, cancellationToken);

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

    public Task<ApiResult<AdminApplicationDetailViewModel>> GetAdminApplicationDetailAsync(
        int applicationId,
        CancellationToken cancellationToken) =>
        GetAsync<AdminApplicationDetailViewModel>($"api/applications/admin/{applicationId}", cancellationToken);

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

    public Task<ApiResult<InstituteAdminViewModel>> SetInstituteActiveAsync(
        int instituteId,
        string rowVersion,
        bool isActive,
        CancellationToken cancellationToken) =>
        PostAsync<InstituteAdminViewModel>(
            $"api/admin/institutes/{instituteId}/{(isActive ? "activate" : "deactivate")}",
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

    public Task<ApiResult<ProgramAdminViewModel>> SetProgramActiveAsync(
        int programId,
        string rowVersion,
        bool isActive,
        CancellationToken cancellationToken) =>
        PostAsync<ProgramAdminViewModel>(
            $"api/admin/programs/{programId}/{(isActive ? "activate" : "deactivate")}",
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
        PutAsync($"api/applications/admin/{model.ApplicationId}/status", new
        {
            model.NewStatus,
            model.RowVersion,
            model.Notes
        }, cancellationToken);

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
}
