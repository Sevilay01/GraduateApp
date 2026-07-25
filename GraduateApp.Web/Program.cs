using System.Globalization;
using GraduateApp.Web.ModelBinding;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var turkishCulture = CultureInfo.GetCultureInfo("tr-TR");
CultureInfo.DefaultThreadCurrentCulture = turkishCulture;
CultureInfo.DefaultThreadCurrentUICulture = turkishCulture;

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "GraduateApp.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = false;
    });
var webDataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("GraduateApp.Web");
if (!builder.Environment.IsDevelopment())
{
    webDataProtection.UseEphemeralDataProtectionProvider();
}

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews(options =>
{
    options.ModelBinderProviders.Insert(0, new SafeDecimalModelBinderProvider());
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    var messages = options.ModelBindingMessageProvider;
    messages.SetMissingBindRequiredValueAccessor(_ => "Bu alan zorunludur.");
    messages.SetMissingKeyOrValueAccessor(() => "Bu alan zorunludur.");
    messages.SetMissingRequestBodyRequiredValueAccessor(() => "Gerekli bilgiler gönderilmedi.");
    messages.SetValueMustNotBeNullAccessor(_ => "Bu alan zorunludur.");
    messages.SetAttemptedValueIsInvalidAccessor((_, _) => "Girilen değer geçerli bir sayı veya tarih biçiminde değil.");
    messages.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => "Girilen değer geçerli bir sayı veya tarih biçiminde değil.");
    messages.SetUnknownValueIsInvalidAccessor(_ => "Girilen değer geçersiz.");
    messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Girilen değer geçersiz.");
    messages.SetValueIsInvalidAccessor(_ => "Girilen değer geçersiz.");
    messages.SetValueMustBeANumberAccessor(_ => "Geçerli bir sayı giriniz.");
    messages.SetNonPropertyValueMustBeANumberAccessor(() => "Geçerli bir sayı giriniz.");
});
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(turkishCulture, turkishCulture);
    options.SupportedCultures = [turkishCulture];
    options.SupportedUICultures = [turkishCulture];
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ApiAccessTokenHandler>();
builder.Services.AddOptions<GraduateApiOptions>()
    .Bind(builder.Configuration.GetSection(GraduateApiOptions.SectionName))
    .Validate(
        options => Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp),
        "GraduateApi:BaseAddress geçerli bir mutlak HTTP(S) adresi olmalıdır.")
    .Validate(
        options => options.TimeoutSeconds is >= 3 and <= 120,
        "GraduateApi:TimeoutSeconds 3 ile 120 arasında olmalıdır.")
    .Validate(
        options => options.InnerDependencyTimeoutSeconds is >= 1 and <= 60,
        "GraduateApi:InnerDependencyTimeoutSeconds 1 ile 60 arasında olmalıdır.")
    .Validate(
        options => options.TimeoutSeconds > options.InnerDependencyTimeoutSeconds,
        "Web API client timeout, API iç bağımlılık timeout değerinden uzun olmalıdır.")
    .ValidateOnStart();
builder.Services.AddHttpClient<GraduateApiClient>((services, client) =>
    {
        var options = services.GetRequiredService<IOptions<GraduateApiOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseAddress, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    })
    .AddHttpMessageHandler<ApiAccessTokenHandler>();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStatusCodePagesWithReExecute("/Home/HttpStatus", "?code={0}");
app.UseRequestLocalization();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();
app.Run();

public partial class Program;
