using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using GraduateApp.API.Models;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection environment variable veya user-secrets ile sağlanmalıdır.");
}

builder.Services.AddDbContext<GraduateAppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddDataProtection().SetApplicationName("GraduateApp.API");
builder.Services.AddProblemDetails();
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddAuthentication(ApiAuthenticationDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiBearerAuthenticationHandler>(ApiAuthenticationDefaults.Scheme, _ => { });
builder.Services.AddAuthorization();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IEmailNormalizer, InvariantEmailNormalizer>();
builder.Services.AddOptions<RegistrationOptions>()
    .Bind(builder.Configuration.GetSection(RegistrationOptions.SectionName))
    .Validate(options => options.MinimumAge is >= 16 and <= 30, "Registration:MinimumAge geçersiz.")
    .Validate(options => options.MaximumAge is >= 60 and <= 120, "Registration:MaximumAge geçersiz.")
    .ValidateOnStart();
builder.Services.AddScoped<StudentRegistrationValidator>();
builder.Services.AddScoped<IPasswordHasher<Student>, PasswordHasher<Student>>();
builder.Services.AddScoped<IPasswordHasher<Admin>, PasswordHasher<Admin>>();
builder.Services.AddScoped<IAccessTokenService, AccessTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IApplicationService, ApplicationService>();
builder.Services.AddScoped<IAdminStudentService, AdminStudentService>();
builder.Services.AddScoped<IProgramOfferingService, ProgramOfferingService>();
builder.Services.AddScoped<IStudentExamScoreService, StudentExamScoreService>();
builder.Services.AddScoped<IStudentProfileService, StudentProfileService>();
builder.Services.AddHostedService<AdminBootstrapHostedService>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IPasswordResetEmailSender, DevelopmentFilePasswordResetEmailSender>();
}
else
{
    builder.Services.AddSingleton<IPasswordResetEmailSender, UnavailablePasswordResetEmailSender>();
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => CreateFixedWindowPartition(context, 5, TimeSpan.FromMinutes(5)));
    options.AddPolicy("register", context => CreateFixedWindowPartition(context, 3, TimeSpan.FromHours(1)));
    options.AddPolicy("password-reset", context => CreateFixedWindowPartition(context, 5, TimeSpan.FromMinutes(15)));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("TrustedWeb", policy => policy
        .WithOrigins(allowedOrigins)
        .WithMethods("GET", "POST", "PUT", "DELETE")
        .WithHeaders("Authorization", "Content-Type")));
}

var app = builder.Build();

app.UseExceptionHandler(exceptionApp => exceptionApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "İşlem tamamlanamadı.",
        Detail = "Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin."
    });
}));
app.UseStatusCodePages(async statusContext =>
{
    var response = statusContext.HttpContext.Response;
    await response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = response.StatusCode,
        Title = response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => "Kimlik doğrulama gerekli.",
            StatusCodes.Status403Forbidden => "Bu işlem için yetkiniz yok.",
            StatusCodes.Status404NotFound => "Kaynak bulunamadı.",
            StatusCodes.Status429TooManyRequests => "Çok fazla istek gönderildi.",
            _ => "İstek tamamlanamadı."
        }
    });
});

app.UseHttpsRedirection();
if (allowedOrigins.Length > 0)
{
    app.UseCors("TrustedWeb");
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

static RateLimitPartition<string> CreateFixedWindowPartition(HttpContext context, int permitLimit, TimeSpan window) =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        });

public partial class Program;
