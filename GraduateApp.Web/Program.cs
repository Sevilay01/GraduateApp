using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// BÖLÜM 1: Tüm kimlik doðrulama ayarlarýný tek bir blokta topluyoruz
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";               // Giriþ yapmayanlarý buraya yönlendir
        options.LogoutPath = "/Account/Logout";             // Çýkýþ yapýldýðýnda yönlendirilecek sayfa
        options.AccessDeniedPath = "/Account/AccessDenied"; // Yetkisi yetmeyenleri (Örn: Admin olmayanlar) yönlendir
        options.Cookie.Name = "GraduateApp.Auth";           // Çerezin tarayýcýdaki ismi
    });


builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// BÖLÜM 2: Middleware sýralamasý
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();