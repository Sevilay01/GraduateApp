using System.Data;
using System.Globalization;
using System.Text.Json;
using GraduateApp.API.Domain;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GraduateApp.API.Services;

public interface IApplicationService
{
    Task<ServiceResult<StudentApplicationDto>> CreateAsync(string studentTc, int programOfferingId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudentApplicationDto>> GetForStudentAsync(string studentTc, CancellationToken cancellationToken);
    Task<StudentApplicationDetailDto?> GetDetailForStudentAsync(string studentTc, Guid publicId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentWorkflowInvariantViolationDto>> GetDocumentWorkflowInvariantViolationsAsync(
        CancellationToken cancellationToken);
    Task<ServiceResult> SubmitAsync(string studentTc, Guid publicId, CancellationToken cancellationToken);
    Task<PagedResult<AdminApplicationListItemDto>> GetForAdminAsync(
        string? search,
        ApplicationStatus? status,
        int? academicYearStart,
        AcademicTerm? term,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
    Task<AdminApplicationDetailDto?> GetDetailForAdminAsync(Guid publicId, CancellationToken cancellationToken);
    Task<ServiceResult> UpdateStatusAsync(Guid publicId, int adminId, ApplicationStatusUpdateDto request, CancellationToken cancellationToken);
}

public sealed class ApplicationService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider) : IApplicationService
{
    public async Task<ServiceResult<StudentApplicationDto>> CreateAsync(
        string studentTc,
        int programOfferingId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var offering = await dbContext.ProgramOfferings
            .Include(item => item.Program).ThenInclude(item => item.Institute)
            .Include(item => item.DocumentRequirements.Where(requirement => requirement.IsActive))
            .Include(item => item.ExamRequirements)
            .Include(item => item.EvaluationCriteria).ThenInclude(item => item.Exam)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.ProgramOfferingId == programOfferingId, cancellationToken);
        if (offering is null)
        {
            return ServiceResult<StudentApplicationDto>.Failure("Dönemsel program ilanı bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (!IsOfferingOpen(offering, now))
        {
            return ServiceResult<StudentApplicationDto>.Failure(
                "Bu ilan şu anda başvuruya açık değil.",
                StatusCodes.Status409Conflict);
        }

        if (offering.DocumentRequirements.Count == 0
            || !offering.DocumentRequirements.Any(item => item.IsRequired))
        {
            return ServiceResult<StudentApplicationDto>.Failure(
                "İlanda en az bir aktif ve zorunlu belge koşulu bulunmalıdır. Lütfen ilan yöneticisiyle iletişime geçin.",
                StatusCodes.Status409Conflict);
        }

        if (offering.UsesEvaluationWorkflow)
        {
            var policyValidation = EvaluationPolicyInvariant.Validate(
                offering.EvaluationCriteria,
                offering.ExamRequirements
                    .Where(item => item.IsRequired)
                    .Select(item => item.ExamId));
            if (!policyValidation.IsValid)
            {
                return ServiceResult<StudentApplicationDto>.Failure(
                    EvaluationPolicyInvariant.DraftCreationError(policyValidation),
                    StatusCodes.Status409Conflict);
            }
        }

        if (await dbContext.Applications.AnyAsync(
            item => item.Tc == studentTc && item.ProgramOfferingId == programOfferingId,
            cancellationToken))
        {
            return ServiceResult<StudentApplicationDto>.Failure(
                "Bu dönemsel ilan için daha önce başvuru veya taslak oluşturulmuş.",
                StatusCodes.Status409Conflict);
        }

        var application = new Application
        {
            PublicId = Guid.NewGuid(),
            Tc = studentTc,
            ProgramOfferingId = programOfferingId,
            ApplicationDate = now,
            CurrentStatus = ApplicationStatus.Draft.ToString(),
            UsesDocumentWorkflow = true,
            UsesEvaluationWorkflow = offering.UsesEvaluationWorkflow,
            DocumentRequirementSnapshots = offering.DocumentRequirements
                .OrderBy(item => item.RequirementId)
                .Select(item => new ApplicationDocumentRequirementSnapshot
                {
                    PublicId = Guid.NewGuid(),
                    SourceRequirementId = item.RequirementId,
                    DocumentCode = item.NormalizedDocumentCode,
                    DisplayName = item.DisplayName,
                    Description = item.Description,
                    IsRequired = item.IsRequired,
                    AllowedContentCategory = item.AllowedContentCategory,
                    MaximumBytes = item.MaximumBytes
                })
                .ToList()
        };
        application.ApplicationStatusHistories.Add(new ApplicationStatusHistory
        {
            PreviousStatus = null,
            StatusName = ApplicationStatus.Draft.ToString(),
            ChangeDate = now,
            Notes = "Belge hazırlığı için taslak oluşturuldu."
        });
        dbContext.Applications.Add(application);
        AddAudit(null, "DocumentDraftCreated", "Application", application.PublicId, new
        {
            application.ProgramOfferingId,
            RequirementCount = application.DocumentRequirementSnapshots.Count
        }, now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ServiceResult<StudentApplicationDto>.Success(
                MapStudentApplication(application, offering),
                StatusCodes.Status201Created);
        }
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return ServiceResult<StudentApplicationDto>.Failure(
                "Taslak oluşturulamadı; ilan koşullarını yeniden kontrol edin.",
                StatusCodes.Status409Conflict);
        }
    }

    public async Task<IReadOnlyList<StudentApplicationDto>> GetForStudentAsync(
        string studentTc,
        CancellationToken cancellationToken)
    {
        var applications = await dbContext.Applications.AsNoTracking()
            .Where(item => item.Tc == studentTc)
            .Include(item => item.ProgramOffering).ThenInclude(item => item.Program)
            .OrderByDescending(item => item.ApplicationDate)
            .ToListAsync(cancellationToken);

        return applications.Select(item => MapStudentApplication(item, item.ProgramOffering)).ToArray();
    }

    public async Task<StudentApplicationDetailDto?> GetDetailForStudentAsync(
        string studentTc,
        Guid publicId,
        CancellationToken cancellationToken)
    {
        var application = await dbContext.Applications.AsNoTracking()
            .Where(item => item.Tc == studentTc && item.PublicId == publicId)
            .Include(item => item.ProgramOffering).ThenInclude(item => item.Program).ThenInclude(item => item.Institute)
            .Include(item => item.DocumentRequirementSnapshots).ThenInclude(item => item.Documents)
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);
        return application is null ? null : MapStudentDetail(application);
    }

    public async Task<ServiceResult> SubmitAsync(
        string studentTc,
        Guid publicId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var application = await dbContext.Applications
            .Include(item => item.ProgramOffering).ThenInclude(item => item.Program).ThenInclude(item => item.Institute)
            .Include(item => item.ProgramOffering).ThenInclude(item => item.ExamRequirements).ThenInclude(item => item.Exam)
            .Include(item => item.ProgramOffering).ThenInclude(item => item.EvaluationCriteria)
            .Include(item => item.DocumentRequirementSnapshots).ThenInclude(item => item.Documents)
            .Include(item => item.ScoreSnapshots)
            .SingleOrDefaultAsync(item => item.PublicId == publicId && item.Tc == studentTc, cancellationToken);
        if (application is null)
        {
            return ServiceResult.Failure("Başvuru bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (!ApplicationStatusRules.TryParseStoredValue(application.CurrentStatus, out var status)
            || status != ApplicationStatus.Draft
            || !application.UsesDocumentWorkflow)
        {
            return ServiceResult.Failure("Yalnızca taslak başvurular gönderilebilir.", StatusCodes.Status409Conflict);
        }

        if (application.DocumentRequirementSnapshots.Count == 0)
        {
            return await BlockSubmissionAsync(
                application,
                "Bu taslak için geçerli belge koşulu bulunmuyor. İlan yöneticisiyle iletişime geçin.",
                "NoRequirementSnapshots",
                transaction,
                cancellationToken);
        }

        if (!application.DocumentRequirementSnapshots.Any(item => item.IsRequired))
        {
            return await BlockSubmissionAsync(
                application,
                "Bu taslak için geçerli zorunlu belge koşulu bulunmuyor. İlan yöneticisiyle iletişime geçin.",
                "NoRequiredRequirementSnapshots",
                transaction,
                cancellationToken);
        }

        var missing = application.DocumentRequirementSnapshots
            .Where(item => item.IsRequired && !item.Documents.Any(document => document.IsCurrent))
            .Select(item => item.DisplayName)
            .OrderBy(item => item)
            .ToArray();
        if (missing.Length > 0)
        {
            return await BlockSubmissionAsync(
                application,
                $"Zorunlu belgeler eksik: {string.Join(", ", missing)}.",
                "MissingRequiredDocuments",
                transaction,
                cancellationToken);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var offering = application.ProgramOffering;
        if (!IsOfferingOpen(offering, now))
        {
            return await BlockSubmissionAsync(
                application,
                "İlan artık başvuruya açık değil.",
                "OfferingClosed",
                transaction,
                cancellationToken);
        }

        var draft = ApplicationStatus.Draft.ToString();
        var withdrawn = ApplicationStatus.Withdrawn.ToString();
        if (!offering.UsesEvaluationWorkflow
            && (offering.Quota <= 0
            || await dbContext.Applications.CountAsync(
                item => item.ProgramOfferingId == offering.ProgramOfferingId
                    && item.ApplicationId != application.ApplicationId
                    && item.CurrentStatus != draft
                    && item.CurrentStatus != withdrawn,
                cancellationToken) >= offering.Quota))
        {
            return await BlockSubmissionAsync(
                application,
                "İlan kontenjanı dolmuştur.",
                "QuotaFull",
                transaction,
                cancellationToken);
        }

        if (await dbContext.Applications.AnyAsync(
            item => item.ApplicationId != application.ApplicationId
                && item.Tc == studentTc
                && item.ProgramOfferingId == offering.ProgramOfferingId,
            cancellationToken))
        {
            return await BlockSubmissionAsync(
                application,
                "Bu dönemsel ilana daha önce başvuru yapılmış.",
                "DuplicateApplication",
                transaction,
                cancellationToken);
        }

        var scoreResult = await BuildScoreSnapshotsAsync(studentTc, offering, now, cancellationToken);
        if (!scoreResult.IsSuccess)
        {
            return await BlockSubmissionAsync(
                application,
                scoreResult.Error!,
                "ExamRequirements",
                transaction,
                cancellationToken);
        }

        ApplicationEvaluation? evaluation = null;
        if (application.UsesEvaluationWorkflow)
        {
            var evaluationResult = await BuildEvaluationSnapshotAsync(
                application,
                offering,
                scoreResult.Value!,
                cancellationToken);
            if (!evaluationResult.IsSuccess)
            {
                return await BlockSubmissionAsync(
                    application,
                    evaluationResult.Error!,
                    "EvaluationPolicy",
                    transaction,
                    cancellationToken);
            }

            evaluation = evaluationResult.Value;
        }

        application.ScoreSnapshots.Clear();
        foreach (var snapshot in scoreResult.Value!)
        {
            application.ScoreSnapshots.Add(snapshot);
        }

        application.Evaluation = evaluation;

        application.ApplicationDate = now;
        application.CurrentStatus = ApplicationStatus.Pending.ToString();
        dbContext.ApplicationStatusHistories.Add(new ApplicationStatusHistory
        {
            ApplicationId = application.ApplicationId,
            PreviousStatus = ApplicationStatus.Draft.ToString(),
            StatusName = ApplicationStatus.Pending.ToString(),
            ChangeDate = now,
            Notes = "Başvuru öğrenci tarafından gönderildi."
        });
        AddAudit(null, "ApplicationSubmitted", "Application", application.PublicId, new
        {
            application.ProgramOfferingId,
            ScoreSnapshotCount = scoreResult.Value!.Count,
            EvaluationComponentCount = application.Evaluation?.Components.Count ?? 0
        }, now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ServiceResult.Success();
        }
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return ServiceResult.Failure(
                "Başvuru gönderilemedi; ilan koşullarını yeniden kontrol edin.",
                StatusCodes.Status409Conflict);
        }
    }

    public async Task<IReadOnlyList<DocumentWorkflowInvariantViolationDto>> GetDocumentWorkflowInvariantViolationsAsync(
        CancellationToken cancellationToken)
    {
        var draft = ApplicationStatus.Draft.ToString();
        var candidates = await dbContext.Applications.AsNoTracking()
            .Where(item => item.UsesDocumentWorkflow && item.CurrentStatus != draft)
            .Select(item => new
            {
                item.PublicId,
                item.CurrentStatus,
                SnapshotCount = item.DocumentRequirementSnapshots.Count,
                RequiredSnapshotCount = item.DocumentRequirementSnapshots.Count(requirement => requirement.IsRequired),
                MissingRequiredDocumentCount = item.DocumentRequirementSnapshots.Count(requirement =>
                    requirement.IsRequired
                    && !requirement.Documents.Any(document => document.IsCurrent))
            })
            .ToListAsync(cancellationToken);

        return candidates
            .Select(item => new DocumentWorkflowInvariantViolationDto(
                item.PublicId,
                ParseStatus(item.CurrentStatus),
                item.SnapshotCount == 0
                    ? "NoRequirementSnapshots"
                    : item.RequiredSnapshotCount == 0
                        ? "NoRequiredRequirementSnapshots"
                        : item.MissingRequiredDocumentCount > 0
                            ? "MissingRequiredDocuments"
                            : string.Empty))
            .Where(item => item.ViolationCategory.Length > 0)
            .OrderBy(item => item.ApplicationPublicId)
            .ToArray();
    }

    public async Task<PagedResult<AdminApplicationListItemDto>> GetForAdminAsync(
        string? search,
        ApplicationStatus? status,
        int? academicYearStart,
        AcademicTerm? term,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);

        var draft = ApplicationStatus.Draft.ToString();
        var query = dbContext.Applications.AsNoTracking()
            .Where(item => item.CurrentStatus != draft)
            .Include(item => item.ProgramOffering).ThenInclude(item => item.Program)
            .Include(item => item.TcNavigation)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            query = query.Where(item =>
                item.ProgramOffering.Program.ProgramName.Contains(searchTerm)
                || item.TcNavigation.StudentName.Contains(searchTerm)
                || item.TcNavigation.StudentSurname.Contains(searchTerm)
                || item.TcNavigation.Email.Contains(searchTerm));
        }

        if (status.HasValue)
        {
            if (status.Value == ApplicationStatus.Draft)
            {
                return new PagedResult<AdminApplicationListItemDto>([], page, pageSize, 0);
            }

            var storedStatus = status.Value.ToString();
            query = query.Where(item => item.CurrentStatus == storedStatus);
        }

        if (academicYearStart.HasValue)
        {
            query = query.Where(item => item.ProgramOffering.AcademicYearStart == academicYearStart.Value);
        }

        if (term.HasValue)
        {
            query = query.Where(item => item.ProgramOffering.Term == term.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var applications = await query
            .OrderByDescending(item => item.ApplicationDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = applications.Select(item => new AdminApplicationListItemDto(
            item.PublicId,
            $"{item.TcNavigation.StudentName} {item.TcNavigation.StudentSurname}".Trim(),
            MaskTc(item.Tc),
            item.ProgramOffering.Program.ProgramName,
            item.ProgramOffering.AcademicYearStart,
            AcademicPeriodFormatter.FormatAcademicYear(item.ProgramOffering.AcademicYearStart),
            item.ProgramOffering.Term,
            AcademicPeriodFormatter.FormatTerm(item.ProgramOffering.Term),
            DateTime.SpecifyKind(item.ApplicationDate, DateTimeKind.Utc),
            ParseStatus(item.CurrentStatus),
            Convert.ToBase64String(item.RowVersion))).ToArray();

        return new PagedResult<AdminApplicationListItemDto>(items, page, pageSize, totalCount);
    }

    public async Task<AdminApplicationDetailDto?> GetDetailForAdminAsync(
        Guid publicId,
        CancellationToken cancellationToken)
    {
        var application = await dbContext.Applications.AsNoTracking()
            .Where(item => item.PublicId == publicId && item.CurrentStatus != ApplicationStatus.Draft.ToString())
            .Include(item => item.ProgramOffering).ThenInclude(item => item.Program).ThenInclude(item => item.Institute)
            .Include(item => item.TcNavigation)
            .Include(item => item.ApplicationStatusHistories)
            .Include(item => item.ScoreSnapshots)
            .Include(item => item.DocumentRequirementSnapshots).ThenInclude(item => item.Documents)
            .SingleOrDefaultAsync(cancellationToken);
        if (application is null)
        {
            return null;
        }

        var history = application.ApplicationStatusHistories
            .OrderByDescending(item => item.ChangeDate)
            .Select(item => new ApplicationStatusHistoryDto(
                TryParseNullableStatus(item.PreviousStatus),
                ParseStatus(item.StatusName),
                DateTime.SpecifyKind(item.ChangeDate, DateTimeKind.Utc),
                item.Notes))
            .ToArray();
        var snapshots = application.ScoreSnapshots
            .OrderBy(item => item.ExamNameSnapshot)
            .Select(item => new ApplicationScoreSnapshotDto(
                item.ExamId,
                item.ExamNameSnapshot,
                item.ScoreSnapshot,
                item.ExamDateSnapshot,
                DateTime.SpecifyKind(item.CapturedAtUtc, DateTimeKind.Utc)))
            .ToArray();
        var offering = application.ProgramOffering;

        return new AdminApplicationDetailDto(
            application.PublicId,
            MaskTc(application.Tc),
            $"{application.TcNavigation.StudentName} {application.TcNavigation.StudentSurname}".Trim(),
            application.TcNavigation.Email,
            offering.Program.ProgramName,
            offering.Program.Institute.InstituteName,
            offering.AcademicYearStart,
            AcademicPeriodFormatter.FormatAcademicYear(offering.AcademicYearStart),
            offering.Term,
            AcademicPeriodFormatter.FormatTerm(offering.Term),
            DateTime.SpecifyKind(application.ApplicationDate, DateTimeKind.Utc),
            ParseStatus(application.CurrentStatus),
            Convert.ToBase64String(application.RowVersion),
            history,
            snapshots,
            application.UsesDocumentWorkflow,
            application.UsesEvaluationWorkflow,
            application.DocumentRequirementSnapshots.OrderBy(item => item.DisplayName).Select(MapAdminRequirement).ToArray());
    }

    public async Task<ServiceResult> UpdateStatusAsync(
        Guid publicId,
        int adminId,
        ApplicationStatusUpdateDto request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var application = await dbContext.Applications
            .SingleOrDefaultAsync(item => item.PublicId == publicId, cancellationToken);
        if (application is null || application.CurrentStatus == ApplicationStatus.Draft.ToString())
        {
            return ServiceResult.Failure("Başvuru bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (!ApplicationStatusRules.TryParseStoredValue(application.CurrentStatus, out var currentStatus)
            || request.NewStatus == ApplicationStatus.Draft
            || !ApplicationStatusRules.CanTransition(currentStatus, request.NewStatus))
        {
            return ServiceResult.Failure("Bu durum geçişine izin verilmiyor.", StatusCodes.Status409Conflict);
        }

        if (application.UsesEvaluationWorkflow
            && request.NewStatus is ApplicationStatus.Approved or ApplicationStatus.Rejected)
        {
            return ServiceResult.Failure(
                "Değerlendirme iş akışındaki başvuru sonucu yalnızca kesinleştirme ve yayımlama işlemiyle belirlenebilir.",
                StatusCodes.Status409Conflict);
        }

        if (request.NewStatus == ApplicationStatus.Approved
            && application.UsesDocumentWorkflow
            && await dbContext.ApplicationDocumentRequirementSnapshots.AnyAsync(requirement =>
                requirement.ApplicationId == application.ApplicationId
                && requirement.IsRequired
                && !requirement.Documents.Any(document =>
                    document.IsCurrent && document.ReviewStatus == DocumentReviewStatus.Approved),
                cancellationToken))
        {
            return ServiceResult.Failure(
                "Tüm zorunlu güncel belgeler onaylanmadan başvuru onaylanamaz.",
                StatusCodes.Status409Conflict);
        }

        byte[] rowVersion;
        try
        {
            rowVersion = Convert.FromBase64String(request.RowVersion);
        }
        catch (FormatException)
        {
            return ServiceResult.Failure("Eş zamanlılık belirteci geçersiz.", StatusCodes.Status400BadRequest);
        }

        dbContext.Entry(application).Property(item => item.RowVersion).OriginalValue = rowVersion;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        application.CurrentStatus = request.NewStatus.ToString();
        dbContext.ApplicationStatusHistories.Add(new ApplicationStatusHistory
        {
            ApplicationId = application.ApplicationId,
            PreviousStatus = currentStatus.ToString(),
            StatusName = request.NewStatus.ToString(),
            ChangedByAdminId = adminId,
            ChangeDate = now,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        });
        AddAudit(adminId, "ApplicationStatusChanged", "Application", application.PublicId, new
        {
            Previous = currentStatus,
            Current = request.NewStatus
        }, now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ServiceResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return ServiceResult.Failure(
                "Başvuru başka bir kullanıcı tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.",
                StatusCodes.Status409Conflict);
        }
    }

    private async Task<ServiceResult<IReadOnlyList<ApplicationScoreSnapshot>>> BuildScoreSnapshotsAsync(
        string studentTc,
        ProgramOffering offering,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var scores = await dbContext.StudentExamScores
            .Where(item => item.Tc == studentTc)
            .Include(item => item.Exam)
            .ToDictionaryAsync(item => item.ExamId, cancellationToken);
        var snapshots = new List<ApplicationScoreSnapshot>();
        foreach (var requirement in offering.ExamRequirements)
        {
            if (!scores.TryGetValue(requirement.ExamId, out var score))
            {
                if (requirement.IsRequired)
                {
                    return ServiceResult<IReadOnlyList<ApplicationScoreSnapshot>>.Failure(
                        $"Başvuru için gerekli {requirement.Exam.ExamName} sınav sonucunuz bulunmuyor. Sınav Sonuçlarım ekranından ekleyiniz.",
                        StatusCodes.Status409Conflict);
                }

                continue;
            }

            if (score.Score < requirement.MinimumScore)
            {
                if (requirement.IsRequired)
                {
                    return ServiceResult<IReadOnlyList<ApplicationScoreSnapshot>>.Failure(
                        $"{requirement.Exam.ExamName} puanınız yetersiz. Başvuru için en az {FormatScore(requirement.MinimumScore)} puan gereklidir.",
                        StatusCodes.Status409Conflict);
                }

                continue;
            }

            if (requirement.MinimumValidityDate.HasValue
                && (!score.ExamDate.HasValue || score.ExamDate.Value < requirement.MinimumValidityDate.Value))
            {
                if (requirement.IsRequired)
                {
                    return ServiceResult<IReadOnlyList<ApplicationScoreSnapshot>>.Failure(
                        $"{requirement.Exam.ExamName} sınav tarihiniz ilan koşulunu sağlamıyor. En erken {requirement.MinimumValidityDate.Value:dd.MM.yyyy} tarihli sonuç gereklidir.",
                        StatusCodes.Status409Conflict);
                }

                continue;
            }

            snapshots.Add(new ApplicationScoreSnapshot
            {
                ExamId = score.ExamId,
                ExamNameSnapshot = score.Exam.ExamName,
                ScoreSnapshot = score.Score,
                ExamDateSnapshot = score.ExamDate,
                CapturedAtUtc = now
            });
        }

        return ServiceResult<IReadOnlyList<ApplicationScoreSnapshot>>.Success(snapshots);
    }

    private async Task<ServiceResult<ApplicationEvaluation>> BuildEvaluationSnapshotAsync(
        Application application,
        ProgramOffering offering,
        IReadOnlyList<ApplicationScoreSnapshot> examSnapshots,
        CancellationToken cancellationToken)
    {
        var criteria = offering.EvaluationCriteria
            .OrderBy(item => item.TieBreakPriority)
            .ThenBy(item => item.CriterionId)
            .ToArray();
        if (criteria.Length == 0
            || criteria.Sum(item => item.WeightBasisPoints) != EvaluationScoring.TotalWeightBasisPoints
            || criteria.Any(item => item.WeightBasisPoints <= 0
                || item.MaximumRawScore <= 0m
                || item.TieBreakPriority <= 0)
            || criteria.Select(item => item.NormalizedCode).Distinct(StringComparer.Ordinal).Count() != criteria.Length
            || criteria.Select(item => item.TieBreakPriority).Distinct().Count() != criteria.Length)
        {
            return ServiceResult<ApplicationEvaluation>.Failure(
                "İlanın değerlendirme politikası geçerli değil; ilan yöneticisiyle iletişime geçin.",
                StatusCodes.Status409Conflict);
        }

        var gpa = criteria.Any(item => item.SourceType == EvaluationCriterionSourceType.UndergraduateGpa)
            ? await dbContext.EducationInfos.AsNoTracking()
                .Where(item => item.Tc == application.Tc)
                .OrderBy(item => item.EducationId)
                .Select(item => item.Gno)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var evaluation = new ApplicationEvaluation
        {
            ProgramOfferingId = offering.ProgramOfferingId,
            EligibilityStatus = EvaluationEligibilityStatus.Pending,
            Components = new List<ApplicationEvaluationComponent>()
        };

        foreach (var criterion in criteria)
        {
            decimal? rawScore = criterion.SourceType switch
            {
                EvaluationCriterionSourceType.UndergraduateGpa => gpa,
                EvaluationCriterionSourceType.ExamScore => examSnapshots
                    .SingleOrDefault(item => item.ExamId == criterion.ExamId)?.ScoreSnapshot,
                EvaluationCriterionSourceType.ManualScore => null,
                _ => null
            };
            if (criterion.SourceType != EvaluationCriterionSourceType.ManualScore && !rawScore.HasValue)
            {
                return ServiceResult<ApplicationEvaluation>.Failure(
                    $"{criterion.DisplayName} için başvuru anında dondurulacak puan bulunamadı.",
                    StatusCodes.Status409Conflict);
            }

            if (rawScore.HasValue && (rawScore.Value < 0m || rawScore.Value > criterion.MaximumRawScore))
            {
                return ServiceResult<ApplicationEvaluation>.Failure(
                    $"{criterion.DisplayName} puanı kriterin 0-{criterion.MaximumRawScore} aralığında değil.",
                    StatusCodes.Status409Conflict);
            }

            var score = rawScore.HasValue
                ? EvaluationScoring.CalculateComponent(
                    rawScore.Value,
                    criterion.MaximumRawScore,
                    criterion.WeightBasisPoints)
                : (EvaluationComponentScore?)null;
            evaluation.Components.Add(new ApplicationEvaluationComponent
            {
                SourceCriterionId = criterion.CriterionId,
                CriterionPublicIdSnapshot = criterion.PublicId,
                CodeSnapshot = criterion.Code,
                DisplayNameSnapshot = criterion.DisplayName,
                SourceTypeSnapshot = criterion.SourceType,
                ExamId = criterion.ExamId,
                RawScore = rawScore,
                MaximumRawScoreSnapshot = criterion.MaximumRawScore,
                NormalizedScore = score?.NormalizedScore,
                WeightBasisPointsSnapshot = criterion.WeightBasisPoints,
                WeightedScore = score?.WeightedScore,
                TieBreakPrioritySnapshot = criterion.TieBreakPriority
            });
        }

        return ServiceResult<ApplicationEvaluation>.Success(evaluation);
    }

    private async Task<ServiceResult> BlockSubmissionAsync(
        Application application,
        string message,
        string reason,
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        AddAudit(null, "DocumentSubmissionBlocked", "Application", application.PublicId, new { Reason = reason }, timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return ServiceResult.Failure(message, StatusCodes.Status409Conflict);
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private static bool IsOfferingOpen(ProgramOffering offering, DateTime now) =>
        offering.Program.IsActive
        && offering.Program.Institute.IsActive
        && !offering.IsArchived
        && offering.IsOpen
        && offering.ApplicationStartUtc.HasValue
        && offering.ApplicationDeadlineUtc.HasValue
        && now >= offering.ApplicationStartUtc.Value
        && now <= offering.ApplicationDeadlineUtc.Value;

    private static StudentApplicationDto MapStudentApplication(Application application, ProgramOffering offering) => new(
        application.PublicId,
        offering.ProgramOfferingId,
        offering.ProgramId,
        offering.Program.ProgramName,
        offering.AcademicYearStart,
        AcademicPeriodFormatter.FormatAcademicYear(offering.AcademicYearStart),
        offering.Term,
        AcademicPeriodFormatter.FormatTerm(offering.Term),
        DateTime.SpecifyKind(application.ApplicationDate, DateTimeKind.Utc),
        ParseStatus(application.CurrentStatus),
        Convert.ToBase64String(application.RowVersion));

    private static StudentApplicationDetailDto MapStudentDetail(Application application)
    {
        var requirements = application.DocumentRequirementSnapshots
            .OrderBy(item => item.DisplayName)
            .Select(item => new ApplicationDocumentRequirementDto(
                item.PublicId,
                item.DocumentCode,
                item.DisplayName,
                item.Description,
                item.IsRequired,
                item.AllowedContentCategory,
                item.MaximumBytes,
                item.Documents.Where(document => document.IsCurrent).Select(MapDocument).SingleOrDefault(),
                []))
            .ToArray();
        return new StudentApplicationDetailDto(
            application.PublicId,
            application.ProgramOffering.Program.ProgramName,
            application.ProgramOffering.Program.Institute.InstituteName,
            application.ProgramOffering.AcademicYearStart,
            AcademicPeriodFormatter.FormatAcademicYear(application.ProgramOffering.AcademicYearStart),
            application.ProgramOffering.Term,
            AcademicPeriodFormatter.FormatTerm(application.ProgramOffering.Term),
            application.ProgramOffering.Quota,
            DateTime.SpecifyKind(application.ApplicationDate, DateTimeKind.Utc),
            ParseStatus(application.CurrentStatus),
            application.UsesDocumentWorkflow,
            application.UsesEvaluationWorkflow,
            requirements,
            requirements.Where(item => item.IsRequired && item.CurrentDocument is null).Select(item => item.DisplayName).ToArray());
    }

    private static ApplicationDocumentRequirementDto MapAdminRequirement(ApplicationDocumentRequirementSnapshot requirement)
    {
        var versions = requirement.Documents.OrderByDescending(item => item.VersionNumber).Select(MapDocument).ToArray();
        return new ApplicationDocumentRequirementDto(
            requirement.PublicId,
            requirement.DocumentCode,
            requirement.DisplayName,
            requirement.Description,
            requirement.IsRequired,
            requirement.AllowedContentCategory,
            requirement.MaximumBytes,
            versions.SingleOrDefault(item => item.IsCurrent),
            versions);
    }

    private static ApplicationDocumentDto MapDocument(ApplicationDocument document) => new(
        document.PublicId,
        document.VersionNumber,
        document.IsCurrent,
        document.OriginalFileName,
        document.VerifiedContentType,
        document.FileSize,
        document.ReviewStatus,
        document.RejectionReason,
        DateTime.SpecifyKind(document.UploadedAtUtc, DateTimeKind.Utc),
        document.ReviewedAtUtc.HasValue ? DateTime.SpecifyKind(document.ReviewedAtUtc.Value, DateTimeKind.Utc) : null,
        Convert.ToBase64String(document.RowVersion));

    private void AddAudit(int? adminId, string eventType, string targetType, Guid targetId, object details, DateTime now) =>
        dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorAdminId = adminId,
            EventType = eventType,
            TargetType = targetType,
            TargetId = targetId.ToString("D"),
            Details = JsonSerializer.Serialize(details),
            CreatedAtUtc = now
        });

    private static ApplicationStatus ParseStatus(string value) =>
        ApplicationStatusRules.TryParseStoredValue(value, out var status) ? status : ApplicationStatus.Pending;

    private static ApplicationStatus? TryParseNullableStatus(string? value) =>
        ApplicationStatusRules.TryParseStoredValue(value, out var status) ? status : null;

    private static string MaskTc(string tc) => tc.Length >= 4 ? $"*******{tc[^4..]}" : "***********";
    private static string FormatScore(decimal score) =>
        score.ToString("0.##", CultureInfo.GetCultureInfo("tr-TR"));
}
