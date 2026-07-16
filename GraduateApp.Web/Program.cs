using Microsoft.AspNetCore.Authentication.Cookies; // BÖLÜM 1: Bunu en üste ekle

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// BÖLÜM 2: Cookie Authentication Servisini Ekle
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login"; // Giriþ yapmayanlarý buraya yönlendir
        options.LogoutPath = "/Account/Logout";
        options.Cookie.Name = "GraduateApp.Auth"; // Çerezin adý
    });
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
app.UseAuthentication(); // BÖLÜM 3: Authentication Middleware'i ekle
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
