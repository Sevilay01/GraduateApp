using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GraduateApp.Tests;

public sealed class GraduateUiFoundationRazorTests
{
    [Fact]
    public async Task Anonymous_home_renders_local_brand_skip_link_active_navigation_and_encoded_content()
    {
        using var host = CreateWebHost();
        const string unsafeProgramName = "<script>alert('program')</script>";
        var html = await RenderMainViewAsync(
            host.Services,
            "Home",
            "Index",
            new HomeViewModel
            {
                OpenPrograms =
                [
                    new ProgramViewModel
                    {
                        ProgramOfferingId = 17,
                        ProgramName = unsafeProgramName,
                        InstituteName = "<b>Fen Bilimleri Enstitüsü</b>",
                        DegreeType = "Yüksek lisans",
                        AcademicYear = "2026–2027",
                        TermName = "Güz",
                        Quota = 12,
                        ApplicationDeadlineUtc = new DateTime(
                            2026,
                            8,
                            24,
                            14,
                            0,
                            0,
                            DateTimeKind.Utc)
                    }
                ]
            });

        Assert.Contains("<html lang=\"tr\">", html, StringComparison.Ordinal);
        Assert.Contains("class=\"skip-link\" href=\"#main-content\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/images/brand/cu_logo_tr.svg\"", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"Çukurova Üniversitesi\"", html, StringComparison.Ordinal);
        Assert.Contains("Öğrenci girişi", html, StringComparison.Ordinal);
        Assert.Contains("Yönetici girişi", html, StringComparison.Ordinal);
        Assert.Contains("Öğrenci kaydı", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Güvenli çıkış", html, StringComparison.Ordinal);
        Assert.Contains(
            "aria-current=\"page\"",
            OpeningTagForLinkText(html, "Ana sayfa"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Fen Bilimleri Enstitüsü</b>", html, StringComparison.Ordinal);
        Assert.Contains(
            "<b>Fen Bilimleri Enstitüsü</b>",
            WebUtility.HtmlDecode(html),
            StringComparison.Ordinal);
        Assert.True(
            html.IndexOf("<h1", StringComparison.Ordinal) < html.IndexOf("<h2", StringComparison.Ordinal),
            "Ana sayfadaki h1, bölüm h2 başlığından önce gelmelidir.");
    }

    [Fact]
    public async Task Public_and_student_program_discovery_forms_preserve_filters_and_render_filtered_empty_state()
    {
        using var host = CreateWebHost();
        var filter = new OpenProgramSearchViewModel
        {
            Search = "<Bilgisayar>",
            AcademicYearStart = 2026,
            Term = AcademicTerm.Fall
        };
        var homeHtml = await RenderMainViewAsync(
            host.Services,
            "Home",
            "Index",
            new HomeViewModel
            {
                ProgramSearch = filter
            });
        var panelHtml = await RenderMainViewAsync(
            host.Services,
            "Panel",
            "Index",
            new PanelDashboardViewModel
            {
                ProgramSearch = filter
            },
            AuthenticatedUser("Student", "Öğrenci"));

        foreach (var html in new[] { homeHtml, panelHtml })
        {
            Assert.Contains("role=\"search\"", html, StringComparison.Ordinal);
            Assert.Contains("method=\"get\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("name=\"search\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"academicYearStart\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"term\"", html, StringComparison.Ordinal);
            Assert.Contains("value=\"2026\"", html, StringComparison.Ordinal);
            Assert.Contains("value=\"Fall\" selected", html, StringComparison.Ordinal);
            Assert.Contains("&lt;Bilgisayar&gt;", html, StringComparison.Ordinal);
            Assert.DoesNotContain("value=\"<Bilgisayar>\"", html, StringComparison.Ordinal);
            Assert.Contains("Filtreleri temizle", html, StringComparison.Ordinal);
            Assert.Contains(
                "Arama ölçütlerine uygun açık program bulunamadı",
                html,
                StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            "name=\"preferUndergraduateProgram\"",
            homeHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "name=\"preferUndergraduateProgram\"",
            panelHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "hiçbir ilana başvurmanızı engellemez",
            panelHtml,
            StringComparison.Ordinal);
        Assert.Contains(
            "derece ekleri eşleştirmeyi değiştirmez",
            panelHtml,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Student_layout_and_panel_render_role_navigation_post_forms_and_antiforgery()
    {
        using var host = CreateWebHost();
        var applicationPublicId = Guid.Parse("821DF0BB-3299-4778-A9CB-E7C1EBD377B1");
        var html = await RenderMainViewAsync(
            host.Services,
            "Panel",
            "Index",
            new PanelDashboardViewModel
            {
                OpenPrograms =
                [
                    new ProgramViewModel
                    {
                        ProgramOfferingId = 42,
                        ProgramName = "Bilgisayar Mühendisliği",
                        InstituteName = "Fen Bilimleri Enstitüsü",
                        DegreeType = "Doktora",
                        AcademicYear = "2026–2027",
                        TermName = "Güz",
                        Quota = 8,
                        IsRecommendedForProfile = true,
                        ApplicationDeadlineUtc = new DateTime(
                            2026,
                            8,
                            24,
                            14,
                            0,
                            0,
                            DateTimeKind.Utc)
                    }
                ],
                Applications =
                [
                    new PanelApplicationViewModel
                    {
                        PublicId = applicationPublicId,
                        ProgramOfferingId = 43,
                        ProgramName = "İstatistik",
                        AcademicYear = "2026–2027",
                        TermName = "Güz",
                        ApplicationDateUtc = new DateTime(
                            2026,
                            7,
                            24,
                            10,
                            0,
                            0,
                            DateTimeKind.Utc),
                        CurrentStatus = ApplicationStatus.Pending
                    }
                ],
                RecommendationMessage = "1 ilan mezuniyet programı adınızla eşleşti.",
                GraduatedProgram = "<Bilgisayar Mühendisliği>"
            },
            AuthenticatedUser("Student", "<Öğrenci Kullanıcı>"));

        Assert.Contains("Başvurularım", html, StringComparison.Ordinal);
        Assert.Contains("Sınav sonuçlarım", html, StringComparison.Ordinal);
        Assert.Contains("Profilim", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Yönetici hesapları", html, StringComparison.Ordinal);
        Assert.Contains(
            "aria-current=\"page\"",
            OpeningTagForLinkText(html, "Başvurularım"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("<Öğrenci Kullanıcı>", html, StringComparison.Ordinal);
        Assert.Contains("<Öğrenci Kullanıcı>", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.Contains("name=\"programOfferingId\" value=\"42\"", html, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.Contains("Program alanı eşleşiyor", html, StringComparison.Ordinal);
        Assert.Contains("data-profile-recommendation", html, StringComparison.Ordinal);
        var decodedHtml = WebUtility.HtmlDecode(html);
        Assert.Contains("<Bilgisayar Mühendisliği>", decodedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Bilgisayar Mühendisliği>", html, StringComparison.Ordinal);
        Assert.Contains("Güvenli çıkış", html, StringComparison.Ordinal);
        Assert.Contains("Öğrenci hesabı", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_and_student_navigation_are_separated_by_role()
    {
        using var host = CreateWebHost();
        var html = await RenderMainViewAsync(
            host.Services,
            "Home",
            "Index",
            new HomeViewModel(),
            AuthenticatedUser("Admin", "Yetkili Kullanıcı"));

        Assert.Contains("Başvurular", html, StringComparison.Ordinal);
        Assert.Contains("İlanlar ve değerlendirme", html, StringComparison.Ordinal);
        Assert.Contains("Akademik katalog", html, StringComparison.Ordinal);
        Assert.Contains("Hesap yönetimi", html, StringComparison.Ordinal);
        Assert.Contains("Öğrenciler", html, StringComparison.Ordinal);
        Assert.Contains("Yöneticiler", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Sınav sonuçlarım", html, StringComparison.Ordinal);
        Assert.Contains("Yönetici hesabı", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_application_list_renders_grouped_navigation_filters_pagination_and_responsive_statuses()
    {
        using var host = CreateWebHost();
        var applicationPublicId = Guid.Parse("58A31172-EE1E-4A77-8CB2-D0E88E049317");
        var html = await RenderMainViewAsync(
            host.Services,
            "Admin",
            "Index",
            new AdminApplicationListViewModel
            {
                Search = "tez adayı",
                Status = ApplicationStatus.Pending,
                AcademicYearStart = 2026,
                Term = AcademicTerm.Fall,
                Result = new PagedResultViewModel<AdminApplicationListItemViewModel>
                {
                    Page = 2,
                    PageSize = 20,
                    TotalCount = 41,
                    TotalPages = 3,
                    Items =
                    [
                        new AdminApplicationListItemViewModel
                        {
                            PublicId = applicationPublicId,
                            StudentFullName = "<script>alert('student')</script>",
                            MaskedTc = "123******90",
                            ProgramName = "Bilgisayar Mühendisliği",
                            AcademicYear = "2026–2027",
                            TermName = "Güz",
                            ApplicationDateUtc = new DateTime(
                                2026,
                                7,
                                24,
                                9,
                                30,
                                0,
                                DateTimeKind.Utc),
                            CurrentStatus = ApplicationStatus.Pending
                        }
                    ]
                }
            },
            AuthenticatedUser("Admin", "Test Yönetici"));

        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains(
            "aria-current=\"page\"",
            OpeningTagForLinkText(html, "Başvurular"),
            StringComparison.Ordinal);
        Assert.Contains("İlanlar ve değerlendirme", decoded, StringComparison.Ordinal);
        Assert.Contains("Akademik katalog", decoded, StringComparison.Ordinal);
        Assert.Contains("Hesap yönetimi", decoded, StringComparison.Ordinal);
        Assert.Contains("Aktif filtreler", decoded, StringComparison.Ordinal);
        Assert.Contains("41 başvuru bulundu", decoded, StringComparison.Ordinal);
        Assert.Contains("responsive-table", html, StringComparison.Ordinal);
        Assert.Contains("data-label=\"Maskelenmiş TC\"", html, StringComparison.Ordinal);
        Assert.Contains("status-badge--pending", html, StringComparison.Ordinal);
        Assert.Contains("page=3", decoded, StringComparison.Ordinal);
        Assert.Contains("pageSize=20", decoded, StringComparison.Ordinal);
        Assert.Contains("search=tez%20aday%C4%B1", decoded, StringComparison.Ordinal);
        Assert.Contains("status=Pending", decoded, StringComparison.Ordinal);
        Assert.Contains("academicYearStart=2026", decoded, StringComparison.Ordinal);
        Assert.Contains("term=Fall", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_document_review_forms_render_only_for_pending_documents_and_keep_concurrency_contract()
    {
        using var host = CreateWebHost();
        var pendingModel = AdminApplicationDetailModel(DocumentReviewStatus.Pending);
        var pendingHtml = await RenderMainViewAsync(
            host.Services,
            "Admin",
            "Detail",
            pendingModel,
            AuthenticatedUser("Admin", "Test Yönetici"));

        Assert.Contains("data-document-review-actions", pendingHtml, StringComparison.Ordinal);
        Assert.Contains("Belgeyi onayla", pendingHtml, StringComparison.Ordinal);
        Assert.Contains("Belgeyi reddet", pendingHtml, StringComparison.Ordinal);
        Assert.Contains("name=\"RowVersion\" value=\"ZG9jdW1lbnQtcm93LXZlcnNpb24=\"", pendingHtml, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", pendingHtml, StringComparison.Ordinal);
        Assert.Contains("Güncel belgeyi güvenli indir", WebUtility.HtmlDecode(pendingHtml), StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", pendingHtml, StringComparison.OrdinalIgnoreCase);

        var approvedModel = AdminApplicationDetailModel(DocumentReviewStatus.Approved);
        var approvedHtml = await RenderMainViewAsync(
            host.Services,
            "Admin",
            "Detail",
            approvedModel,
            AuthenticatedUser("Admin", "Test Yönetici"));

        Assert.DoesNotContain("data-document-review-actions", approvedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"ReviewStatus\" value=\"Approved\"", approvedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"ReviewStatus\" value=\"Rejected\"", approvedHtml, StringComparison.Ordinal);
        Assert.Contains("data-document-review-readonly", approvedHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offering_management_renders_closed_creation_readiness_concurrency_and_safe_requirement_actions()
    {
        using var host = CreateWebHost();
        var requirementPublicId = Guid.Parse("D7A5DF16-0A47-4B17-AEC4-655A24052277");
        var model = new ProgramOfferingPageViewModel
        {
            Offerings =
            [
                new ProgramOfferingAdminViewModel
                {
                    ProgramOfferingId = 7,
                    ProgramId = 11,
                    ProgramName = "<img src=x onerror=alert(1)>",
                    InstituteName = "Fen Bilimleri Enstitüsü",
                    DegreeType = "Doktora",
                    AcademicYear = "2026–2027",
                    TermName = "Güz",
                    ApplicationStartUtc = new DateTime(2026, 8, 1, 7, 0, 0, DateTimeKind.Utc),
                    ApplicationDeadlineUtc = new DateTime(2026, 8, 24, 14, 0, 0, DateTimeKind.Utc),
                    Quota = 10,
                    IsOpen = true,
                    UsesDocumentWorkflow = true,
                    UsesEvaluationWorkflow = true,
                    EvaluationState = OfferingEvaluationState.Configuring,
                    DocumentRequirementCount = 0,
                    ActiveRequiredDocumentRequirementCount = 0,
                    HasActiveRequiredDocumentRequirement = false,
                    DraftApplicationCount = 1,
                    DocumentConfigurationHealth =
                        OfferingDocumentConfigurationHealth.OpenInvalidWithDrafts,
                    RowVersion = "b2ZmZXJpbmctcm93LXZlcnNpb24="
                },
                new ProgramOfferingAdminViewModel
                {
                    ProgramOfferingId = 8,
                    ProgramId = 12,
                    ProgramName = "İstatistik",
                    InstituteName = "Fen Bilimleri Enstitüsü",
                    DegreeType = "Tezli Yüksek Lisans",
                    AcademicYear = "2026–2027",
                    TermName = "Bahar",
                    Quota = 8,
                    IsOpen = true,
                    UsesDocumentWorkflow = true,
                    UsesEvaluationWorkflow = false,
                    DocumentRequirementCount = 1,
                    ActiveRequiredDocumentRequirementCount = 1,
                    HasActiveRequiredDocumentRequirement = true,
                    DocumentConfigurationHealth = OfferingDocumentConfigurationHealth.OpenHealthy,
                    RowVersion = "c2Vjb25kLW9mZmVyaW5nLXJvdw=="
                },
                new ProgramOfferingAdminViewModel
                {
                    ProgramOfferingId = 9,
                    ProgramId = 12,
                    ProgramName = "Yayımlanmış Riskli İlan",
                    InstituteName = "Fen Bilimleri Enstitüsü",
                    DegreeType = "Tezli Yüksek Lisans",
                    AcademicYear = "2025–2026",
                    TermName = "Güz",
                    Quota = 5,
                    IsOpen = true,
                    UsesDocumentWorkflow = true,
                    UsesEvaluationWorkflow = true,
                    EvaluationState = OfferingEvaluationState.Published,
                    DocumentRequirementCount = 0,
                    ActiveRequiredDocumentRequirementCount = 0,
                    HasActiveRequiredDocumentRequirement = false,
                    SubmittedOrLaterApplicationCount = 1,
                    DocumentConfigurationHealth =
                        OfferingDocumentConfigurationHealth.OpenInvalidWithSubmittedApplications,
                    RowVersion = "cHVibGlzaGVkLW9mZmVyaW5nLXJvdw=="
                }
            ],
            Form = new ProgramOfferingFormViewModel
            {
                ProgramId = 11,
                AcademicYearStart = 2026,
                Term = AcademicTerm.Fall,
                ApplicationStartLocal = new DateTime(2026, 8, 1, 10, 0, 0),
                ApplicationDeadlineLocal = new DateTime(2026, 8, 24, 17, 0, 0),
                Quota = 10,
                Programs =
                [
                    new ProgramCatalogItemViewModel
                    {
                        ProgramId = 11,
                        ProgramName = "Bilgisayar Mühendisliği",
                        InstituteName = "Fen Bilimleri Enstitüsü",
                        DegreeType = "Doktora"
                    },
                    new ProgramCatalogItemViewModel
                    {
                        ProgramId = 12,
                        ProgramName = "İstatistik",
                        InstituteName = "Fen Bilimleri Enstitüsü",
                        DegreeType = "Tezli Yüksek Lisans"
                    }
                ],
                Exams = [new ExamCatalogItemViewModel { ExamId = 3, ExamName = "ALES" }],
                ExamRequirements = [new ProgramOfferingRequirementInputViewModel { ExamId = 3 }]
            },
            RequirementOfferingId = 8,
            DocumentRequirements = new Dictionary<int, IReadOnlyList<OfferingDocumentRequirementViewModel>>
            {
                [8] =
                [
                    new OfferingDocumentRequirementViewModel
                    {
                        PublicId = requirementPublicId,
                        DocumentCode = "TRANSCRIPT",
                        DisplayName = "Transkript",
                        Description = "Onaylı transkript",
                        IsRequired = true,
                        IsActive = true,
                        AllowedContentCategory = DocumentContentCategory.PdfOnly,
                        MaximumBytes = 5 * 1024 * 1024,
                        RowVersion = "cmVxdWlyZW1lbnQtcm93LXZlcnNpb24="
                    }
                ]
            },
            DocumentRequirementForm = new OfferingDocumentRequirementFormViewModel
            {
                ProgramOfferingId = 8,
                MaximumBytes = 5 * 1024 * 1024
            }
        };

        var html = await RenderMainViewAsync(
            host.Services,
            "Admin",
            "Offerings",
            model,
            AuthenticatedUser("Admin", "Test Yönetici"));
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("data-new-offering-closed", html, StringComparison.Ordinal);
        Assert.Contains("Yeni ilan kapalı oluşturulacak", decoded, StringComparison.Ordinal);
        Assert.True(
            decoded.IndexOf("Yeni ilan oluştur", StringComparison.Ordinal)
            < decoded.IndexOf("İlanları filtrele", StringComparison.Ordinal));
        Assert.Contains("Eksik · 0 koşul", decoded, StringComparison.Ordinal);
        Assert.Contains("Hazır · 1 koşul", decoded, StringComparison.Ordinal);
        Assert.Contains("İlan açılmadan önce en az bir zorunlu belge koşulu tanımlayın", decoded, StringComparison.Ordinal);
        Assert.Contains("Toplam ağırlık 10.000 bp", decoded, StringComparison.Ordinal);
        Assert.Contains("Belge koşulu değişiklikleri yalnızca yeni taslakları etkiler", decoded, StringComparison.Ordinal);
        Assert.Contains("İlan yapılandırma sağlığı", decoded, StringComparison.Ordinal);
        Assert.Contains("data-health-category=\"D\"", html, StringComparison.Ordinal);
        Assert.Contains("data-health-category=\"E\"", html, StringComparison.Ordinal);
        Assert.Contains("data-remediation-close-form", html, StringComparison.Ordinal);
        Assert.Contains("CloseInvalidOfferingForRemediation", html, StringComparison.Ordinal);
        Assert.Contains("Kapat ve manuel incelemeye al", decoded, StringComparison.Ordinal);
        Assert.Contains("Kesinleştirilmiş veya yayımlanmış değerlendirme", decoded, StringComparison.Ordinal);
        Assert.Contains(
            "Değerlendirme kesinleştirildiği veya yayımlandığı için ilan yapılandırması kilitlidir.",
            decoded,
            StringComparison.Ordinal);
        Assert.Contains("name=\"rowVersion\" value=\"cHVibGlzaGVkLW9mZmVyaW5nLXJvdw==\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("requirementOfferingId=9", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("editId=9", decoded, StringComparison.Ordinal);
        Assert.Contains("Belge koşullarını yapılandır", decoded, StringComparison.Ordinal);
        Assert.Contains(
            "Yeni belge koşulları mevcut başvuru snapshot’larını değiştirmez",
            decoded,
            StringComparison.Ordinal);
        Assert.Contains(
            "Belge koşullarını, ilgili enstitünün güncel ilan kılavuzuna göre tanımlayın",
            decoded,
            StringComparison.Ordinal);
        Assert.Contains("sosyalbilimler.cu.edu.tr", html, StringComparison.Ordinal);
        Assert.Contains("iso.cu.edu.tr", html, StringComparison.Ordinal);
        Assert.Contains("data-selected-requirement-offering", html, StringComparison.Ordinal);
        Assert.Contains("Seçili ilan · #8", decoded, StringComparison.Ordinal);
        Assert.Contains("requirementOfferingId=8", decoded, StringComparison.Ordinal);
        Assert.Contains("#document-requirements", decoded, StringComparison.Ordinal);
        Assert.Contains("editId=7", decoded, StringComparison.Ordinal);
        Assert.Contains("#exam-requirements-heading", decoded, StringComparison.Ordinal);
        Assert.Contains("#offering-readiness-heading", decoded, StringComparison.Ordinal);
        Assert.Contains("Koşulu pasifleştir", decoded, StringComparison.Ordinal);
        Assert.Contains("name=\"rowVersion\" value=\"cmVxdWlyZW1lbnQtcm93LXZlcnNpb24=\"", html, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.Contains("responsive-table", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html, StringComparison.Ordinal);

        var remediationForms = Regex.Matches(
            html,
            "<form\\b[^>]*data-remediation-close-form[^>]*>.*?</form>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        var publishedRemediationForm = Assert.Single(
            remediationForms.Cast<Match>(),
            match => match.Value.Contains(
                "cHVibGlzaGVkLW9mZmVyaW5nLXJvdw==",
                StringComparison.Ordinal))
            .Value;
        Assert.Contains("method=\"post\"", publishedRemediationForm, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CloseInvalidOfferingForRemediation", publishedRemediationForm, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", publishedRemediationForm, StringComparison.Ordinal);
        var remediationFieldNames = Regex.Matches(
                publishedRemediationForm,
                "name=\"([^\"]+)\"",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(remediationFieldNames.SetEquals(
        [
            "programOfferingId",
            "rowVersion",
            "academicYearStart",
            "term",
            "includeArchived",
            "__RequestVerificationToken"
        ]));

        var editableRequirements = model.DocumentRequirements[8];
        model.RequirementOfferingId = 9;
        model.DocumentRequirements =
            new Dictionary<int, IReadOnlyList<OfferingDocumentRequirementViewModel>>
            {
                [9] =
                [
                    new OfferingDocumentRequirementViewModel
                    {
                        PublicId = Guid.Parse("F03B23D5-4B41-4A57-B99D-5DF2B3799C86"),
                        DocumentCode = "HISTORICAL",
                        DisplayName = "Tarihsel belge",
                        IsRequired = true,
                        IsActive = true,
                        AllowedContentCategory = DocumentContentCategory.PdfOnly,
                        MaximumBytes = 1024,
                        RowVersion = "bG9ja2VkLXJlcXVpcmVtZW50"
                    }
                ]
            };
        model.DocumentRequirementForm = new OfferingDocumentRequirementFormViewModel
        {
            ProgramOfferingId = 9,
            PublicId = Guid.Parse("F03B23D5-4B41-4A57-B99D-5DF2B3799C86"),
            DocumentCode = "HISTORICAL",
            DisplayName = "Tarihsel belge",
            MaximumBytes = 1024,
            RowVersion = "bG9ja2VkLXJlcXVpcmVtZW50"
        };
        var lockedRequirementHtml = await RenderMainViewAsync(
            host.Services,
            "Admin",
            "Offerings",
            model,
            AuthenticatedUser("Admin", "Test Yönetici"));
        var decodedLockedRequirementHtml = WebUtility.HtmlDecode(lockedRequirementHtml);

        Assert.Contains("data-document-requirements-read-only", lockedRequirementHtml, StringComparison.Ordinal);
        Assert.Contains(
            "Değerlendirme kesinleştirildiği veya yayımlandığı için ilan yapılandırması kilitlidir.",
            decodedLockedRequirementHtml,
            StringComparison.Ordinal);
        Assert.Contains("HISTORICAL", decodedLockedRequirementHtml, StringComparison.Ordinal);
        Assert.Contains("Salt okunur", decodedLockedRequirementHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveDocumentRequirement", lockedRequirementHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("SetDocumentRequirementActive", lockedRequirementHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("editRequirementId", decodedLockedRequirementHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("requirementOfferingId=9", decodedLockedRequirementHtml, StringComparison.Ordinal);

        model.RequirementOfferingId = 8;
        model.DocumentRequirements =
            new Dictionary<int, IReadOnlyList<OfferingDocumentRequirementViewModel>>
            {
                [8] = editableRequirements
            };
        model.DocumentRequirementForm = new OfferingDocumentRequirementFormViewModel
        {
            ProgramOfferingId = 8,
            MaximumBytes = 5 * 1024 * 1024
        };
        model.Form.ProgramOfferingId = 7;
        model.Form.RowVersion = "b2ZmZXJpbmctcm93LXZlcnNpb24=";
        model.AutoFocusTarget = "offering-form-heading";
        var editHtml = await RenderMainViewAsync(
            host.Services,
            "Admin",
            "Offerings",
            model,
            AuthenticatedUser("Admin", "Test Yönetici"));
        var decodedEditHtml = WebUtility.HtmlDecode(editHtml);

        Assert.Contains("data-selected-offering-context", editHtml, StringComparison.Ordinal);
        Assert.Contains("Düzenlenen ilan:", decodedEditHtml, StringComparison.Ordinal);
        Assert.Contains("İlan #7", decodedEditHtml, StringComparison.Ordinal);
        Assert.Contains(
            "data-auto-focus=\"true\"",
            editHtml,
            StringComparison.Ordinal);
        var generalIndex = decodedEditHtml.IndexOf("Genel bilgiler</a>", StringComparison.Ordinal);
        var documentIndex = decodedEditHtml.IndexOf("Belge koşulları</a>", generalIndex, StringComparison.Ordinal);
        var examIndex = decodedEditHtml.IndexOf("Sınav koşulları</a>", documentIndex, StringComparison.Ordinal);
        var evaluationIndex = decodedEditHtml.IndexOf("Değerlendirme kriterleri</a>", examIndex, StringComparison.Ordinal);
        var readinessIndex = decodedEditHtml.IndexOf("Gözden geçir ve aç</a>", evaluationIndex, StringComparison.Ordinal);
        Assert.True(generalIndex >= 0);
        Assert.True(documentIndex > generalIndex);
        Assert.True(examIndex > documentIndex);
        Assert.True(evaluationIndex > examIndex);
        Assert.True(readinessIndex > evaluationIndex);
    }

    [Fact]
    public async Task Configuring_evaluation_renders_mutations_exam_invariants_quota_boundary_and_encoded_candidates()
    {
        using var host = CreateWebHost();
        var model = EvaluationPageModel(OfferingEvaluationState.Configuring);
        var html = await RenderMainViewAsync(
            host.Services,
            "Admin",
            "Evaluation",
            model,
            AuthenticatedUser("Admin", "Test Yönetici"));
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("Yapılandırılıyor", decoded, StringComparison.Ordinal);
        Assert.Contains("10.000 / 10.000 bp", decoded, StringComparison.Ordinal);
        Assert.Contains("SaveEvaluationCriterion", html, StringComparison.Ordinal);
        Assert.Contains("DeleteEvaluationCriterion", html, StringComparison.Ordinal);
        Assert.Contains("DecideEligibility", html, StringComparison.Ordinal);
        Assert.Contains("SetManualScore", html, StringComparison.Ordinal);
        Assert.Contains("FinalizeEvaluation", html, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.Contains("ALES · En az 55,5", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("İlan dışı sınav", decoded, StringComparison.Ordinal);

        var gpaForm = FormContaining(html, "name=\"Code\" value=\"GPA\"");
        Assert.Contains("data-exam-field hidden=\"hidden\"", gpaForm, StringComparison.Ordinal);
        Assert.Contains("name=\"ExamId\"", gpaForm, StringComparison.Ordinal);
        Assert.Contains("disabled=\"disabled\"", gpaForm, StringComparison.Ordinal);
        Assert.DoesNotContain("required=\"required\"", gpaForm, StringComparison.Ordinal);
        Assert.Contains("Kontenjan çizgisi", decoded, StringComparison.Ordinal);
        Assert.Contains("responsive-table", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert('candidate')</script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OfferingEvaluationState.Finalized)]
    [InlineData(OfferingEvaluationState.Published)]
    public async Task Locked_evaluation_lifecycle_omits_all_mutation_forms(OfferingEvaluationState state)
    {
        using var host = CreateWebHost();
        var model = EvaluationPageModel(state);
        model.Evaluation.Capabilities = new EvaluationCapabilitiesViewModel
        {
            CanPublish = state == OfferingEvaluationState.Finalized
        };

        var html = await RenderMainViewAsync(
            host.Services,
            "Admin",
            "Evaluation",
            model,
            AuthenticatedUser("Admin", "Test Yönetici"));
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("data-readonly-evaluation-criterion", html, StringComparison.Ordinal);
        Assert.Contains("data-readonly-eligibility", html, StringComparison.Ordinal);
        Assert.Contains("data-readonly-evaluation-components", html, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveEvaluationCriterion", html, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteEvaluationCriterion", html, StringComparison.Ordinal);
        Assert.DoesNotContain("DecideEligibility", html, StringComparison.Ordinal);
        Assert.DoesNotContain("SetManualScore", html, StringComparison.Ordinal);
        Assert.DoesNotContain("FinalizeEvaluation", html, StringComparison.Ordinal);

        if (state == OfferingEvaluationState.Finalized)
        {
            Assert.Contains("Sonuçlar kesinleştirildi; değerlendirme verileri değiştirilemez.", decoded, StringComparison.Ordinal);
            Assert.Contains("Yayımlama onayına geç", decoded, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("Sonuçlar yayımlandı; değerlendirme verileri değiştirilemez.", decoded, StringComparison.Ordinal);
            Assert.DoesNotContain("Yayımlama onayına geç", decoded, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Publish_confirmation_renders_final_ranking_atomic_warning_post_and_row_version()
    {
        using var host = CreateWebHost();
        var model = EvaluationPageModel(OfferingEvaluationState.Finalized);
        var html = await RenderMainViewAsync(
            host.Services,
            "Admin",
            "PublishEvaluation",
            new EvaluationPublishPageViewModel
            {
                ProgramOfferingId = model.Evaluation.ProgramOfferingId,
                ProgramName = model.Evaluation.ProgramName,
                Evaluation = model.Evaluation,
                Summary = new EvaluationPublicationSummaryViewModel
                {
                    Quota = 1,
                    AdmittedCount = 1,
                    NotAdmittedCount = 1,
                    IneligibleCount = 0,
                    OfferingRowVersion = "cHVibGlzaC1yb3ctdmVyc2lvbg=="
                }
            },
            AuthenticatedUser("Admin", "Test Yönetici"));
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("data-publish-confirmation", html, StringComparison.Ordinal);
        Assert.Contains("Kesinleştirilmiş sıralama", decoded, StringComparison.Ordinal);
        Assert.Contains("Yayımlama öncesinde öğrenciler puan, sıra ve sonucu göremez", decoded, StringComparison.Ordinal);
        Assert.Contains("history, audit ve sonuç snapshot", decoded, StringComparison.Ordinal);
        Assert.Contains("İşlem geri alınamaz", decoded, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"rowVersion\" value=\"cHVibGlzaC1yb3ctdmVyc2lvbg==\"", html, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.Contains(">Sonuçları yayımla</button>", html, StringComparison.Ordinal);
        Assert.Contains("responsive-table", html, StringComparison.Ordinal);
        Assert.DoesNotContain("window.confirm", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert('candidate')</script>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Catalog_and_account_errors_render_encoded_responsive_and_specific_safe_actions()
    {
        using var host = CreateWebHost();
        var universitiesHtml = await RenderMainViewAsync(
            host.Services,
            "Admin",
            "Universities",
            new UniversityPageViewModel
            {
                ErrorMessage = "409: Aynı ad kullanılıyor <script>alert('duplicate')</script>",
                Universities =
                [
                    new UniversityViewModel
                    {
                        UniversityId = 4,
                        UniversityName = "<img src=x onerror=alert(1)>"
                    }
                ]
            },
            AuthenticatedUser("Admin", "Test Yönetici"));

        Assert.Contains("409: Aynı ad kullanılıyor", WebUtility.HtmlDecode(universitiesHtml), StringComparison.Ordinal);
        Assert.Contains("responsive-table", universitiesHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", universitiesHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img src=x", universitiesHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", universitiesHtml, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", universitiesHtml, StringComparison.Ordinal);

        var accountsHtml = await RenderMainViewAsync(
            host.Services,
            "Admin",
            "Accounts",
            new AdminAccountPageViewModel
            {
                Result = new PagedResultViewModel<AdminAccountViewModel>
                {
                    Page = 1,
                    PageSize = 20,
                    TotalCount = 1,
                    TotalPages = 1,
                    Items =
                    [
                        new AdminAccountViewModel
                        {
                            PublicId = Guid.Parse("B6A0B01B-39FD-4D91-A99F-A23681C6FDD2"),
                            Email = "admin@example.test",
                            IsActive = true,
                            CreatedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow,
                            RowVersion = "YWRtaW4tcm93LXZlcnNpb24="
                        }
                    ]
                }
            },
            AuthenticatedUser("Admin", "current-admin@example.test"));

        Assert.Contains("Son aktif ve davetini tamamlamış yönetici pasifleştirilemez", WebUtility.HtmlDecode(accountsHtml), StringComparison.Ordinal);
        Assert.Contains("Yöneticiyi pasifleştir", WebUtility.HtmlDecode(accountsHtml), StringComparison.Ordinal);
        Assert.Contains("name=\"rowVersion\" value=\"YWRtaW4tcm93LXZlcnNpb24=\"", accountsHtml, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", accountsHtml, StringComparison.Ordinal);
        Assert.Contains("responsive-table", accountsHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Account_views_render_stable_field_names_antiforgery_and_distinct_admin_context()
    {
        using var host = CreateWebHost();
        var studentLogin = await RenderMainViewAsync(
            host.Services,
            "Account",
            "Login",
            new LoginViewModel { AccountType = LoginAccountType.Student });
        var adminLogin = await RenderMainViewAsync(
            host.Services,
            "Account",
            "AdminLogin",
            new LoginViewModel { AccountType = LoginAccountType.Admin },
            viewName: "Login");
        var register = await RenderMainViewAsync(
            host.Services,
            "Account",
            "Register",
            new RegisterViewModel());

        foreach (var fieldName in new[] { "ReturnUrl", "Username", "Password", "RememberMe" })
        {
            Assert.Contains($"name=\"{fieldName}\"", studentLogin, StringComparison.Ordinal);
        }

        Assert.Contains("__RequestVerificationToken", studentLogin, StringComparison.Ordinal);
        Assert.Contains("auth-shell--student", studentLogin, StringComparison.Ordinal);
        Assert.Contains("auth-shell--admin", adminLogin, StringComparison.Ordinal);
        Assert.Contains(
            "yalnızca yetkilendirilmiş",
            WebUtility.HtmlDecode(adminLogin),
            StringComparison.Ordinal);
        Assert.Contains("name=\"Username\"", adminLogin, StringComparison.Ordinal);
        Assert.Contains("name=\"Password\"", adminLogin, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", adminLogin, StringComparison.Ordinal);

        foreach (var fieldName in new[]
                 {
                     "Tc",
                     "FirstName",
                     "LastName",
                     "FatherName",
                     "BirthDate",
                     "Email",
                     "Telephone",
                     "Password",
                     "ConfirmPassword"
                 })
        {
            Assert.Contains($"name=\"{fieldName}\"", register, StringComparison.Ordinal);
        }

        Assert.Contains("__RequestVerificationToken", register, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Access_denied_renders_safe_role_specific_account_switch_actions()
    {
        using var host = CreateWebHost();
        var studentHtml = await RenderMainViewAsync<object?>(
            host.Services,
            "Account",
            "AccessDenied",
            null,
            AuthenticatedUser("Student", "Test Öğrenci"));
        var adminHtml = await RenderMainViewAsync<object?>(
            host.Services,
            "Account",
            "AccessDenied",
            null,
            AuthenticatedUser("Admin", "Test Yönetici"));
        var anonymousHtml = await RenderMainViewAsync<object?>(
            host.Services,
            "Account",
            "AccessDenied",
            null);

        Assert.Contains("action=\"/Account/SwitchAccount\"", studentHtml, StringComparison.Ordinal);
        Assert.Contains("name=\"accountType\" value=\"Admin\"", studentHtml, StringComparison.Ordinal);
        Assert.Contains("Yönetici hesabıyla giriş yap", WebUtility.HtmlDecode(studentHtml), StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", studentHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("returnUrl", studentHtml, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("action=\"/Account/SwitchAccount\"", adminHtml, StringComparison.Ordinal);
        Assert.Contains("name=\"accountType\" value=\"Student\"", adminHtml, StringComparison.Ordinal);
        Assert.Contains("Öğrenci hesabıyla giriş yap", WebUtility.HtmlDecode(adminHtml), StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", adminHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("returnUrl", adminHtml, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("SwitchAccount", anonymousHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"/Account/Login\"", anonymousHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"/Account/AdminLogin\"", anonymousHtml, StringComparison.Ordinal);
        Assert.Matches("href=\"/(?:Home(?:/Index)?)?\"[^>]*>Ana sayfaya dön</a>", anonymousHtml);
    }

    [Fact]
    public async Task Application_detail_hides_unpublished_result_and_preserves_secure_document_forms()
    {
        using var host = CreateWebHost();
        var applicationPublicId = Guid.Parse("457EA57D-6F18-4B19-87FB-86C4BC9FBB3A");
        var requirementPublicId = Guid.Parse("8B26C8C0-03A7-41DB-A7FA-AFF66B4A3F72");
        var html = await RenderMainViewAsync(
            host.Services,
            "Panel",
            "ApplicationDetail",
            new StudentApplicationDetailViewModel
            {
                PublicId = applicationPublicId,
                InstituteName = "Fen Bilimleri Enstitüsü",
                ProgramName = "Biyoteknoloji",
                AcademicYear = "2026–2027",
                TermName = "Güz",
                ApplicationDeadlineUtc = new DateTime(2026, 8, 24, 14, 0, 0, DateTimeKind.Utc),
                CanUpdateDocuments = true,
                CurrentStatus = ApplicationStatus.Draft,
                UsesEvaluationWorkflow = true,
                UsesDocumentWorkflow = true,
                PublishedEvaluation = null,
                DocumentRequirements =
                [
                    new ApplicationDocumentRequirementViewModel
                    {
                        PublicId = requirementPublicId,
                        DisplayName = "Transkript",
                        IsRequired = true,
                        AllowedContentCategory = DocumentContentCategory.PdfOnly,
                        MaximumBytes = 5 * 1024 * 1024
                    }
                ],
                MissingRequiredDocuments = ["Transkript"]
            },
            AuthenticatedUser("Student", "Test Öğrenci"));

        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains(
            "puan, sıralama ve karar gösterilmez",
            decoded,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Yayımlanmış değerlendirme sonucu", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("Toplam puan", decoded, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"step\"", html, StringComparison.Ordinal);
        Assert.Contains("enctype=\"multipart/form-data\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"file\"", html, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.Contains("Başvuruyu gönder", decoded, StringComparison.Ordinal);
        Assert.Contains("disabled=\"disabled\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Student_application_lifecycle_controls_are_capability_gated_and_antiforgery_protected()
    {
        using var host = CreateWebHost();
        var publicId = Guid.Parse("AD1E8726-F34B-4836-959A-EB280EC3FD91");
        var model = new StudentApplicationDetailViewModel
        {
            PublicId = publicId,
            InstituteName = "Fen Bilimleri Enstitüsü",
            ProgramName = "Bilgisayar Mühendisliği",
            AcademicYear = "2026–2027",
            TermName = "Güz",
            CurrentStatus = ApplicationStatus.UnderReview,
            RowVersion = "AQIDBA==",
            CanWithdraw = true,
            UsesEvaluationWorkflow = true
        };

        var withdrawHtml = await RenderMainViewAsync(
            host.Services,
            "Panel",
            "ApplicationDetail",
            model,
            AuthenticatedUser("Student", "Test Öğrenci"));
        var decodedWithdrawHtml = WebUtility.HtmlDecode(withdrawHtml);

        Assert.Contains("Başvuruyu geri çek", decodedWithdrawHtml, StringComparison.Ordinal);
        Assert.Contains($"/Panel/Applications/{publicId:D}/Withdraw", withdrawHtml, StringComparison.Ordinal);
        Assert.Contains("name=\"rowVersion\" value=\"AQIDBA==\"", withdrawHtml, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", withdrawHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("/Reactivate", withdrawHtml, StringComparison.Ordinal);

        model.CurrentStatus = ApplicationStatus.Withdrawn;
        model.CanWithdraw = false;
        model.CanReactivate = true;
        var reactivateHtml = await RenderMainViewAsync(
            host.Services,
            "Panel",
            "ApplicationDetail",
            model,
            AuthenticatedUser("Student", "Test Öğrenci"));
        var decodedReactivateHtml = WebUtility.HtmlDecode(reactivateHtml);

        Assert.Contains("Taslak olarak yeniden etkinleştir", decodedReactivateHtml, StringComparison.Ordinal);
        Assert.Contains("Geri çekildi", decodedReactivateHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Değerlendirme sürüyor", decodedReactivateHtml, StringComparison.Ordinal);
        Assert.Contains($"/Panel/Applications/{publicId:D}/Reactivate", reactivateHtml, StringComparison.Ordinal);
        Assert.Contains("name=\"rowVersion\" value=\"AQIDBA==\"", reactivateHtml, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", reactivateHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("/Withdraw", reactivateHtml, StringComparison.Ordinal);

        model.CurrentStatus = ApplicationStatus.Approved;
        model.CanReactivate = false;
        var terminalHtml = await RenderMainViewAsync(
            host.Services,
            "Panel",
            "ApplicationDetail",
            model,
            AuthenticatedUser("Student", "Test Öğrenci"));
        Assert.DoesNotContain("/Withdraw", terminalHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("/Reactivate", terminalHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submitted_application_renders_document_replacement_only_while_capability_is_open()
    {
        using var host = CreateWebHost();
        var model = new StudentApplicationDetailViewModel
        {
            PublicId = Guid.Parse("6ABCE270-B3D5-4ACD-8149-A454B69F2912"),
            InstituteName = "Fen Bilimleri Enstitüsü",
            ProgramName = "Bilgisayar Mühendisliği",
            AcademicYear = "2026–2027",
            TermName = "Güz",
            ApplicationDeadlineUtc = new DateTime(2026, 8, 24, 14, 0, 0, DateTimeKind.Utc),
            CanUpdateDocuments = true,
            CurrentStatus = ApplicationStatus.UnderReview,
            UsesDocumentWorkflow = true,
            DocumentRequirements =
            [
                new ApplicationDocumentRequirementViewModel
                {
                    PublicId = Guid.Parse("C5749038-25D1-46A1-9FE0-C70675BF1DA1"),
                    DisplayName = "Transkript",
                    IsRequired = true,
                    AllowedContentCategory = DocumentContentCategory.PdfOnly,
                    MaximumBytes = 5 * 1024 * 1024,
                    CurrentDocument = new ApplicationDocumentViewModel
                    {
                        PublicId = Guid.Parse("3FB80AEC-59D1-4B3F-9853-E08965FBE355"),
                        VersionNumber = 1,
                        IsCurrent = true,
                        OriginalFileName = "transkript.pdf",
                        VerifiedContentType = "application/pdf",
                        FileSize = 1024,
                        ReviewStatus = DocumentReviewStatus.Approved,
                        UploadedAtUtc = new DateTime(2026, 7, 30, 7, 0, 0, DateTimeKind.Utc)
                    }
                }
            ]
        };

        var openHtml = await RenderMainViewAsync(
            host.Services,
            "Panel",
            "ApplicationDetail",
            model,
            AuthenticatedUser("Student", "Test Öğrenci"));
        var decodedOpenHtml = WebUtility.HtmlDecode(openHtml);

        Assert.Contains("Belgeyi güncelle", decodedOpenHtml, StringComparison.Ordinal);
        Assert.Contains("mevcut onay geçersiz olur", decodedOpenHtml, StringComparison.Ordinal);
        Assert.Contains("enctype=\"multipart/form-data\"", openHtml, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", openHtml, StringComparison.Ordinal);

        model.CanUpdateDocuments = false;
        var closedHtml = await RenderMainViewAsync(
            host.Services,
            "Panel",
            "ApplicationDetail",
            model,
            AuthenticatedUser("Student", "Test Öğrenci"));
        var decodedClosedHtml = WebUtility.HtmlDecode(closedHtml);

        Assert.Contains("belgeler artık güncellenemez", decodedClosedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("enctype=\"multipart/form-data\"", closedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("mevcut onay geçersiz olur", decodedClosedHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Published_result_renders_hierarchy_quota_snapshot_and_encoded_components()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var ambientCulture = CultureInfo.GetCultureInfo("en-US");

        try
        {
            CultureInfo.CurrentCulture = ambientCulture;
            CultureInfo.CurrentUICulture = ambientCulture;

            using var host = CreateWebHost();
            var html = await RenderMainViewAsync(
                host.Services,
                "Panel",
                "ApplicationDetail",
                new StudentApplicationDetailViewModel
                {
                    PublicId = Guid.Parse("0A3D7446-50D0-4709-A597-792180A66958"),
                    InstituteName = "Sosyal Bilimler Enstitüsü",
                    ProgramName = "<img src=x onerror=alert(1)>",
                    AcademicYear = "2026–2027",
                    TermName = "Bahar",
                    Quota = 7,
                    CurrentStatus = ApplicationStatus.Approved,
                    UsesEvaluationWorkflow = true,
                    PublishedEvaluation = new PublishedApplicationEvaluationViewModel
                    {
                        Outcome = EvaluationOutcome.Admitted,
                        TotalScore = 88.1250m,
                        Rank = 3,
                        ResultsPublishedAtUtc = new DateTime(
                            2026,
                            7,
                            24,
                            9,
                            0,
                            0,
                            DateTimeKind.Utc),
                        Components =
                        [
                            new PublishedEvaluationComponentViewModel
                            {
                                DisplayName = "<script>alert('criterion')</script>",
                                RawScore = 90.5m,
                                NormalizedScore = 90.25m,
                                WeightBasisPoints = 5000,
                                WeightedScore = 45.125m
                            }
                        ]
                    }
                },
                AuthenticatedUser("Student", "Test Öğrenci"));

            Assert.Equal(ambientCulture, CultureInfo.CurrentCulture);
            Assert.Equal(ambientCulture, CultureInfo.CurrentUICulture);

            var decoded = WebUtility.HtmlDecode(html);
            Assert.Contains("Yayımlanmış değerlendirme sonucu", decoded, StringComparison.Ordinal);
            Assert.Contains("Toplam puan", decoded, StringComparison.Ordinal);
            Assert.Contains("88,1250", decoded, StringComparison.Ordinal);
            Assert.Contains(">90,5000<", html, StringComparison.Ordinal);
            Assert.Contains(">90,2500<", html, StringComparison.Ordinal);
            Assert.Contains(">45,1250<", html, StringComparison.Ordinal);
            Assert.Contains("Sıralama", decoded, StringComparison.Ordinal);
            Assert.Contains("Kontenjan", decoded, StringComparison.Ordinal);
            Assert.Contains(">7<", html, StringComparison.Ordinal);
            Assert.Contains("snapshot verilerine dayanır", decoded, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>alert", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<img src=x", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
            Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void Accessibility_styles_define_visible_focus_and_reduced_motion_behavior()
    {
        var css = File.ReadAllText(
            Path.Combine(
                RepositoryRoot(),
                "GraduateApp.Web",
                "wwwroot",
                "css",
                "site.css"));

        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
        Assert.Contains("--cu-focus-ring", css, StringComparison.Ordinal);
        Assert.Contains(".configuration-steps", css, StringComparison.Ordinal);
        Assert.Contains("max-width: 100%;", css, StringComparison.Ordinal);
        Assert.Contains(".admin-item-card__actions .btn", css, StringComparison.Ordinal);

        var siteJavaScript = File.ReadAllText(
            Path.Combine(
                RepositoryRoot(),
                "GraduateApp.Web",
                "wwwroot",
                "js",
                "site.js"));
        Assert.Contains("focusRequestedPageContext", siteJavaScript, StringComparison.Ordinal);
        Assert.Contains("data-auto-focus=\"true\"", siteJavaScript, StringComparison.Ordinal);
        Assert.Contains("scrollIntoView", siteJavaScript, StringComparison.Ordinal);
    }

    private static IHost CreateWebHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureWebHostDefaults(builder => builder
                .UseEnvironment("Development")
                .UseContentRoot(Path.Combine(RepositoryRoot(), "GraduateApp.Web"))
                .ConfigureServices(services =>
                {
                    services.AddDataProtection().UseEphemeralDataProtectionProvider();
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(HomeController).Assembly);
                })
                .Configure(_ => { }))
            .Build();

    private static ClaimsPrincipal AuthenticatedUser(string role, string name) =>
        new(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, name),
                    new Claim(ClaimTypes.Role, role)
                ],
                "GraduateUiFoundationTest"));

    private static async Task<string> RenderMainViewAsync<TModel>(
        IServiceProvider services,
        string controller,
        string action,
        TModel model,
        ClaimsPrincipal? user = null,
        string? viewName = null)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var renderCulture = CultureInfo.GetCultureInfo("tr-TR");

        try
        {
            CultureInfo.CurrentCulture = renderCulture;
            CultureInfo.CurrentUICulture = renderCulture;

            using var scope = services.CreateScope();
            var scopedServices = scope.ServiceProvider;
            var httpContext = new DefaultHttpContext
            {
                RequestServices = scopedServices,
                User = user ?? new ClaimsPrincipal(new ClaimsIdentity())
            };
            httpContext.Features.Set<IRequestCultureFeature>(
                new RequestCultureFeature(new RequestCulture(renderCulture), provider: null));
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost");
            var routeData = new RouteData();
            routeData.Values["controller"] = controller;
            routeData.Values["action"] = action;
            routeData.Routers.Add(new TestRouter());
            var actionContext = new ActionContext(
                httpContext,
                routeData,
                new ActionDescriptor(),
                new ModelStateDictionary());
            var viewEngine = scopedServices.GetRequiredService<ICompositeViewEngine>();
            var viewResult = viewEngine.FindView(
                actionContext,
                viewName ?? action,
                isMainPage: true);
            Assert.True(
                viewResult.Success,
                $"{controller}/{viewName ?? action} view bulunamadı: "
                + string.Join(", ", viewResult.SearchedLocations ?? []));
            var viewData = new ViewDataDictionary<TModel>(
                scopedServices.GetRequiredService<IModelMetadataProvider>(),
                actionContext.ModelState)
            {
                Model = model
            };
            var tempData = new TempDataDictionary(
                httpContext,
                scopedServices.GetRequiredService<ITempDataProvider>());
            using var writer = new StringWriter(renderCulture);
            var viewContext = new ViewContext(
                actionContext,
                viewResult.View,
                viewData,
                tempData,
                writer,
                new HtmlHelperOptions());

            await viewResult.View.RenderAsync(viewContext);
            return writer.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static AdminApplicationDetailViewModel AdminApplicationDetailModel(
        DocumentReviewStatus reviewStatus)
    {
        var document = new ApplicationDocumentViewModel
        {
            PublicId = Guid.Parse("C9F0AFA6-778E-4580-A374-A167BB8212BD"),
            VersionNumber = 2,
            IsCurrent = true,
            OriginalFileName = "<script>alert('document')</script>.pdf",
            VerifiedContentType = "application/pdf",
            FileSize = 1024,
            ReviewStatus = reviewStatus,
            RejectionReason = reviewStatus == DocumentReviewStatus.Rejected ? "Belge okunamıyor." : null,
            UploadedAtUtc = new DateTime(2026, 7, 23, 8, 0, 0, DateTimeKind.Utc),
            RowVersion = "ZG9jdW1lbnQtcm93LXZlcnNpb24="
        };

        return new AdminApplicationDetailViewModel
        {
            PublicId = Guid.Parse("E3D70BD7-7392-4138-928B-BD6D5D4F7E93"),
            MaskedTc = "123******90",
            StudentFullName = "<script>alert('student')</script>",
            Email = "student@example.test",
            ProgramName = "Bilgisayar Mühendisliği",
            InstituteName = "Fen Bilimleri Enstitüsü",
            AcademicYear = "2026–2027",
            TermName = "Güz",
            ApplicationDateUtc = new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Utc),
            CurrentStatus = ApplicationStatus.Pending,
            RowVersion = "YXBwbGljYXRpb24tcm93LXZlcnNpb24=",
            UsesDocumentWorkflow = true,
            History =
            [
                new ApplicationStatusHistoryViewModel
                {
                    CurrentStatus = ApplicationStatus.Pending,
                    ChangedAtUtc = new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Utc),
                    Notes = "Başvuru gönderildi."
                }
            ],
            ScoreSnapshots =
            [
                new ApplicationScoreSnapshotViewModel
                {
                    ExamId = 6,
                    ExamName = "ALES",
                    Score = 82.5m,
                    ExamDate = new DateOnly(2026, 5, 10)
                }
            ],
            DocumentRequirements =
            [
                new ApplicationDocumentRequirementViewModel
                {
                    PublicId = Guid.Parse("7B1DAEA8-8F04-4901-9161-2FAB6904BF64"),
                    DocumentCode = "TRANSCRIPT",
                    DisplayName = "Transkript",
                    Description = "Onaylı transkript",
                    IsRequired = true,
                    AllowedContentCategory = DocumentContentCategory.PdfOnly,
                    MaximumBytes = 5 * 1024 * 1024,
                    CurrentDocument = document,
                    Versions = [document]
                }
            ]
        };
    }

    private static EvaluationPageViewModel EvaluationPageModel(OfferingEvaluationState state)
    {
        var examCriterionPublicId = Guid.Parse("8E9705E1-93DA-4F88-A87F-CCEE0256BD98");
        var gpaCriterionPublicId = Guid.Parse("A78C457B-9CEE-44D1-898C-A12B279DE4C1");
        var manualCriterionPublicId = Guid.Parse("B52BA6B3-E29D-4DF2-B245-CFA27BD1C99F");
        var firstApplicationPublicId = Guid.Parse("61F9339B-FFCE-46F1-9A02-BAA35E12C5B7");
        var secondApplicationPublicId = Guid.Parse("F621BF25-B6B0-4E65-88FC-683239056C29");

        IReadOnlyList<EvaluationComponentViewModel> Components(decimal manualScore) =>
        [
            new EvaluationComponentViewModel
            {
                CriterionPublicId = examCriterionPublicId,
                Code = "ALES",
                DisplayName = "ALES",
                SourceType = EvaluationCriterionSourceType.ExamScore,
                RawScore = 80m,
                NormalizedScore = 80m,
                WeightBasisPoints = 5000,
                WeightedScore = 40m,
                TieBreakPriority = 1,
                RowVersion = "YWxlcy1yb3ctdmVyc2lvbg=="
            },
            new EvaluationComponentViewModel
            {
                CriterionPublicId = gpaCriterionPublicId,
                Code = "GPA",
                DisplayName = "Lisans GNO",
                SourceType = EvaluationCriterionSourceType.UndergraduateGpa,
                RawScore = 3.5m,
                MaximumRawScore = 4m,
                NormalizedScore = 87.5m,
                WeightBasisPoints = 2500,
                WeightedScore = 21.875m,
                TieBreakPriority = 2,
                RowVersion = "Z3BhLXJvdy12ZXJzaW9u"
            },
            new EvaluationComponentViewModel
            {
                CriterionPublicId = manualCriterionPublicId,
                Code = "INTERVIEW",
                DisplayName = "Mülakat",
                SourceType = EvaluationCriterionSourceType.ManualScore,
                RawScore = manualScore,
                MaximumRawScore = 100m,
                NormalizedScore = manualScore,
                WeightBasisPoints = 2500,
                WeightedScore = manualScore / 4m,
                TieBreakPriority = 3,
                RowVersion = "bWFudWFsLXJvdy12ZXJzaW9u"
            }
        ];

        return new EvaluationPageViewModel
        {
            Evaluation = new AdminEvaluationViewModel
            {
                ProgramOfferingId = 44,
                ProgramName = "Bilgisayar Mühendisliği",
                AcademicYear = "2026–2027",
                TermName = "Güz",
                ApplicationDeadlineUtc = new DateTime(2026, 8, 24, 14, 0, 0, DateTimeKind.Utc),
                Quota = 1,
                IsOpen = false,
                UsesEvaluationWorkflow = true,
                EvaluationState = state,
                OfferingRowVersion = "ZXZhbHVhdGlvbi1vZmZlcmluZy1yb3c=",
                Capabilities = state == OfferingEvaluationState.Configuring
                    ? new EvaluationCapabilitiesViewModel
                    {
                        CanEditPolicy = true,
                        CanDecideEligibility = true,
                        CanEditManualScore = true,
                        CanFinalize = true
                    }
                    : new EvaluationCapabilitiesViewModel(),
                EligibleExamRequirements =
                [
                    new ExamRequirementViewModel
                    {
                        ExamId = 6,
                        ExamName = "ALES",
                        MinimumScore = 55.5m,
                        IsRequired = true
                    }
                ],
                Criteria =
                [
                    new EvaluationCriterionViewModel
                    {
                        PublicId = examCriterionPublicId,
                        Code = "ALES",
                        DisplayName = "ALES",
                        SourceType = EvaluationCriterionSourceType.ExamScore,
                        ExamId = 6,
                        ExamName = "ALES",
                        WeightBasisPoints = 5000,
                        MaximumRawScore = 100m,
                        TieBreakPriority = 1,
                        RowVersion = "YWxlcy1jcml0ZXJpb24tcm93"
                    },
                    new EvaluationCriterionViewModel
                    {
                        PublicId = gpaCriterionPublicId,
                        Code = "GPA",
                        DisplayName = "Lisans GNO",
                        SourceType = EvaluationCriterionSourceType.UndergraduateGpa,
                        WeightBasisPoints = 2500,
                        MaximumRawScore = 4m,
                        TieBreakPriority = 2,
                        RowVersion = "Z3BhLWNyaXRlcmlvbi1yb3c="
                    },
                    new EvaluationCriterionViewModel
                    {
                        PublicId = manualCriterionPublicId,
                        Code = "INTERVIEW",
                        DisplayName = "Mülakat",
                        SourceType = EvaluationCriterionSourceType.ManualScore,
                        WeightBasisPoints = 2500,
                        MaximumRawScore = 100m,
                        TieBreakPriority = 3,
                        RowVersion = "bWFudWFsLWNyaXRlcmlvbi1yb3c="
                    }
                ],
                Applications =
                [
                    new AdminEvaluationApplicationViewModel
                    {
                        ApplicationPublicId = firstApplicationPublicId,
                        StudentFullName = "<script>alert('candidate')</script>",
                        MaskedTc = "123******90",
                        CurrentStatus = ApplicationStatus.UnderReview,
                        DocumentReviewSummary = "Belgeler onaylandı",
                        EligibilityStatus = EvaluationEligibilityStatus.Eligible,
                        TotalScore = 83.75m,
                        Rank = 1,
                        Outcome = EvaluationOutcome.Admitted,
                        EvaluationRowVersion = "Zmlyc3QtYXBwbGljYXRpb24tcm93",
                        Components = Components(87.5m)
                    },
                    new AdminEvaluationApplicationViewModel
                    {
                        ApplicationPublicId = secondApplicationPublicId,
                        StudentFullName = "İkinci Aday",
                        MaskedTc = "987******10",
                        CurrentStatus = ApplicationStatus.UnderReview,
                        DocumentReviewSummary = "Belgeler onaylandı",
                        EligibilityStatus = EvaluationEligibilityStatus.Eligible,
                        TotalScore = 79.375m,
                        Rank = 2,
                        Outcome = EvaluationOutcome.NotAdmitted,
                        EvaluationRowVersion = "c2Vjb25kLWFwcGxpY2F0aW9uLXJvdw==",
                        Components = Components(70m)
                    }
                ]
            },
            Preview = new EvaluationRankingPreviewViewModel
            {
                CanFinalize = true,
                Rows =
                [
                    new EvaluationRankingRowViewModel
                    {
                        ApplicationPublicId = firstApplicationPublicId,
                        StudentFullName = "<script>alert('candidate')</script>",
                        MaskedTc = "123******90",
                        TotalScore = 83.75m,
                        Rank = 1,
                        ProjectedOutcome = EvaluationOutcome.Admitted
                    },
                    new EvaluationRankingRowViewModel
                    {
                        ApplicationPublicId = secondApplicationPublicId,
                        StudentFullName = "İkinci Aday",
                        MaskedTc = "987******10",
                        TotalScore = 79.375m,
                        Rank = 2,
                        ProjectedOutcome = EvaluationOutcome.NotAdmitted
                    }
                ]
            },
            CriterionForm = new EvaluationCriterionFormViewModel
            {
                ProgramOfferingId = 44,
                SourceType = EvaluationCriterionSourceType.ManualScore,
                MaximumRawScore = 100m,
                TieBreakPriority = 4
            }
        };
    }

    private static string FormContaining(string html, string marker)
    {
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Form işareti bulunamadı: {marker}");
        var formStart = html.LastIndexOf("<form", markerIndex, StringComparison.Ordinal);
        var formEnd = html.IndexOf("</form>", markerIndex, StringComparison.Ordinal);
        Assert.True(formStart >= 0 && formEnd >= formStart, $"Form sınırları bulunamadı: {marker}");
        return html[formStart..(formEnd + "</form>".Length)];
    }

    private static string OpeningTagForLinkText(string html, string text)
    {
        var textIndex = html.IndexOf($">{text}</a>", StringComparison.Ordinal);
        Assert.True(textIndex >= 0, $"Bağlantı metni bulunamadı: {text}");
        var tagStart = html.LastIndexOf("<a", textIndex, StringComparison.Ordinal);
        var tagEnd = html.IndexOf('>', tagStart);
        Assert.True(tagStart >= 0 && tagEnd > tagStart);
        return html[tagStart..(tagEnd + 1)];
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private sealed class TestRouter : IRouter
    {
        public Task RouteAsync(RouteContext context) => Task.CompletedTask;

        public VirtualPathData GetVirtualPath(VirtualPathContext context)
        {
            var path = $"/{context.Values["controller"]}/{context.Values["action"]}";
            var query = context.Values
                .Where(item => item.Key is not "controller" and not "action" && item.Value is not null)
                .Select(item =>
                    $"{Uri.EscapeDataString(item.Key)}="
                    + Uri.EscapeDataString(Convert.ToString(item.Value, CultureInfo.InvariantCulture) ?? string.Empty));
            var queryString = string.Join("&", query);
            return new VirtualPathData(this, string.IsNullOrEmpty(queryString) ? path : $"{path}?{queryString}");
        }
    }
}
