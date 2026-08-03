using GraduateApp.Web.Localization;
using GraduateApp.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace GraduateApp.Tests;

public sealed class BilingualStudentUiTests
{
    [Fact]
    public void English_request_localizes_public_student_and_application_workflow_text()
    {
        var context = CreateContext("en-US");

        Assert.Equal("Student registration", UiText.Get(context, "Register.Title"));
        Assert.Equal("Student dashboard", UiText.Get(context, "Panel.Title"));
        Assert.Equal("Profile and education information", UiText.Get(context, "Profile.Title"));
        Assert.Equal("My exam scores", UiText.Get(context, "ExamScores.Title"));
        Assert.Equal("Application details", UiText.Get(context, "ApplicationDetail.Title"));
        Assert.Equal("Application progress", UiText.Get(context, "Progress.Title"));
        Assert.Equal(
            "You do not have permission to access this page",
            UiText.Get(context, "AccessDenied.Heading"));
        Assert.Equal(
            "Computer Engineering",
            UiText.SelectLocalized(context, "Bilgisayar Mühendisliği", "Computer Engineering"));
    }

    [Fact]
    public void Localized_workflow_presentations_keep_enum_contracts_out_of_views()
    {
        var english = CreateContext("en-US");
        var turkish = CreateContext("tr-TR");

        Assert.Equal("Under review", UiText.LocalizeApplicationStatus(english, ApplicationStatus.UnderReview));
        Assert.Equal("İnceleniyor", UiText.LocalizeApplicationStatus(turkish, ApplicationStatus.UnderReview));
        Assert.Equal("Admitted", UiText.LocalizeEvaluationOutcome(english, EvaluationOutcome.Admitted));
        Assert.Equal("Kabul", UiText.LocalizeEvaluationOutcome(turkish, EvaluationOutcome.Admitted));
        Assert.Equal("Pending review", UiText.LocalizeDocumentReview(english, DocumentReviewStatus.Pending));
        Assert.Equal("PDF, JPEG, or PNG", UiText.LocalizeDocumentCategory(english, DocumentContentCategory.PdfOrImage));
    }

    [Fact]
    public void Turkish_resources_preserve_critical_recommendation_and_safe_navigation_copy()
    {
        var turkish = CreateContext("tr-TR");

        Assert.Equal(
            "Program alanı eşleşiyor",
            UiText.Get(turkish, "Panel.ProfileMatch"));
        Assert.Equal(
            "Program adları karşılaştırılır; ilandaki tezli/tezsiz yüksek lisans veya doktora gibi derece ekleri eşleştirmeyi değiştirmez. Resmî başvuru uygunluğu kararı değildir ve hiçbir ilana başvurmanızı engellemez.",
            UiText.Get(turkish, "Panel.PreferHelp"));
        Assert.Equal(
            "Ana sayfaya dön",
            UiText.Get(turkish, "AccessDenied.Home"));
        Assert.Equal(
            "Sonuçlar kesinleştirilip yayımlanana kadar puan, sıralama ve karar gösterilmez.",
            UiText.Get(turkish, "ApplicationDetail.EvaluationPendingLead"));
        Assert.Equal(
            "Bu taslak için geçerli zorunlu belge koşulu bulunmuyor. İlan yöneticisiyle iletişime geçin.",
            UiText.Get(turkish, "ApplicationDetail.InvalidConfiguration"));
        Assert.Equal(
            "Profil değişiklikleri mevcut başvuruların değerlendirme verilerini etkilemez.",
            UiText.Get(turkish, "Profile.SnapshotMessage"));
    }

    [Fact]
    public void Missing_english_catalog_name_falls_back_to_turkish_without_hiding_the_program()
    {
        var english = CreateContext("en-US");

        Assert.Equal(
            "Bilgisayar Mühendisliği",
            UiText.SelectLocalized(english, "Bilgisayar Mühendisliği", null));
        Assert.Equal(
            "Bilgisayar Mühendisliği",
            UiText.SelectLocalized(english, "Bilgisayar Mühendisliği", " "));
    }

    private static DefaultHttpContext CreateContext(string culture)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IRequestCultureFeature>(
            new RequestCultureFeature(new RequestCulture(culture), new CookieRequestCultureProvider()));
        return context;
    }
}
