using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ApiAccessTokenHandler>();
builder.Services.AddHttpClient<GraduateApiClient>((services, client) =>
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var baseAddress = configuration["GraduateApi:BaseAddress"];
        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var apiUri)
            || (apiUri.Scheme != Uri.UriSchemeHttps && apiUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "GraduateApi:BaseAddress appsettings veya environment variable ile sağlanmalıdır.");
        }

        client.BaseAddress = apiUri;
        client.Timeout = TimeSpan.FromSeconds(15);
    })
    .AddHttpMessageHandler<ApiAccessTokenHandler>();

var app = builder.Build();

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
