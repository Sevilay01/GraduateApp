using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using GraduateApp.API.Domain;
using GraduateApp.API.Infrastructure;
using GraduateApp.API.Models;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection environment variable veya user-secrets ile sağlanmalıdır.");
}

builder.Services.AddDatabaseTimeoutOptions(builder.Configuration);
builder.Services.AddDbContext<GraduateAppDbContext>((services, options) =>
{
    var timeouts = services.GetRequiredService<IOptions<DatabaseTimeoutOptions>>().Value;
    var databaseTarget = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
    {
        ConnectTimeout = timeouts.ConnectionSeconds
    };
    options.UseSqlServer(
        databaseTarget.ConnectionString,
        sqlServer => sqlServer.CommandTimeout(timeouts.CommandSeconds));
});
var apiDataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("GraduateApp.API");
if (!builder.Environment.IsDevelopment())
{
    apiDataProtection.UseEphemeralDataProtectionProvider();
}
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions[CorrelationIdMiddleware.ProblemDetailsExtensionName] =
            CorrelationIdMiddleware.Get(context.HttpContext);
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
var configuredUploadMaximum = builder.Configuration.GetValue<long?>(
    $"{DocumentUploadOptions.SectionName}:MaximumBytes") ?? DocumentWorkflowCatalog.DefaultMaximumUploadBytes;
builder.Services.AddOptions<DocumentUploadOptions>()
    .Bind(builder.Configuration.GetSection(DocumentUploadOptions.SectionName))
    .Validate(
        options => options.MaximumBytes is > 0 and <= DocumentWorkflowCatalog.AbsoluteMaximumUploadBytes,
        "DocumentUpload:MaximumBytes 1 bayt ile 100 MB arasında olmalıdır.")
    .ValidateOnStart();
builder.Services.AddOptions<DocumentStorageOptions>()
    .Bind(builder.Configuration.GetSection(DocumentStorageOptions.SectionName));
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = checked(configuredUploadMaximum + 64 * 1024));
builder.Services.AddControllers(options =>
    {
        options.Filters.Add<CorrelationProblemDetailsFilter>();
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
    })
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var validationMessages = context.ModelState.Values
            .SelectMany(entry => entry.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? "Girilen değer geçersiz."
                : error.ErrorMessage)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new BadRequestObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Gönderilen bilgiler doğrulanamadı.",
            Detail = validationMessages.Length == 0
                ? "Girilen değerleri kontrol edip tekrar deneyiniz."
                : string.Join(' ', validationMessages),
            Extensions =
            {
                [CorrelationIdMiddleware.ProblemDetailsExtensionName] =
                    CorrelationIdMiddleware.Get(context.HttpContext)
            }
        });
    };
});

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
builder.Services.AddOptions<AdminInvitationOptions>()
    .Bind(builder.Configuration.GetSection(AdminInvitationOptions.SectionName))
    .Validate(options => options.LifetimeHours is >= 1 and <= 168, "AdminInvitation:LifetimeHours 1 ile 168 arasında olmalıdır.")
    .ValidateOnStart();
builder.Services.AddScoped<StudentRegistrationValidator>();
builder.Services.AddScoped<IPasswordHasher<Student>, PasswordHasher<Student>>();
builder.Services.AddScoped<IPasswordHasher<Admin>, PasswordHasher<Admin>>();
builder.Services.AddScoped<IAccessTokenService, AccessTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IApplicationService, ApplicationService>();
builder.Services.AddScoped<IApplicationDocumentService, ApplicationDocumentService>();
builder.Services.AddScoped<IDocumentFileValidator, DocumentFileValidator>();
builder.Services.AddScoped<IAdminStudentService, AdminStudentService>();
builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();
builder.Services.AddScoped<IInstituteAdminService, InstituteAdminService>();
builder.Services.AddScoped<IProgramAdminService, ProgramAdminService>();
builder.Services.AddScoped<IUniversityCatalogService, UniversityCatalogService>();
builder.Services.AddScoped<IProgramOfferingService, ProgramOfferingService>();
builder.Services.AddScoped<IEvaluationPolicyService, EvaluationPolicyService>();
builder.Services.AddScoped<IApplicationEvaluationService, ApplicationEvaluationService>();
builder.Services.AddScoped<IOfferingDocumentRequirementService, OfferingDocumentRequirementService>();
builder.Services.AddScoped<IStudentExamScoreService, StudentExamScoreService>();
builder.Services.AddScoped<IStudentProfileService, StudentProfileService>();
builder.Services.AddHostedService<AdminBootstrapHostedService>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IPasswordResetEmailSender, DevelopmentFilePasswordResetEmailSender>();
    builder.Services.AddSingleton<IAdminInvitationEmailSender, DevelopmentFileAdminInvitationEmailSender>();
    builder.Services.AddSingleton<IPrivateFileStorage, DevelopmentPrivateFileStorage>();
    builder.Services.AddSingleton<IFileMalwareScanner, DevelopmentNoOpFileMalwareScanner>();
}
else
{
    builder.Services.AddSingleton<IPasswordResetEmailSender, UnavailablePasswordResetEmailSender>();
    builder.Services.AddSingleton<IAdminInvitationEmailSender, UnavailableAdminInvitationEmailSender>();
    builder.Services.AddSingleton<IPrivateFileStorage, UnavailablePrivateFileStorage>();
    builder.Services.AddSingleton<IFileMalwareScanner, UnavailableFileMalwareScanner>();
}

builder.Services.TryAddSingleton<IDataProtectionReadinessProbe, DefaultDataProtectionReadinessProbe>();
builder.Services.TryAddSingleton<ISqlServerReadinessProbe, SqlServerReadinessProbe>();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<SqlServerReadinessHealthCheck>("sql", tags: ["ready"])
    .AddCheck<ProviderReadinessHealthCheck>("providers", tags: ["ready"]);

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
        .WithHeaders("Authorization", "Content-Type", CorrelationIdMiddleware.HeaderName)
        .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));
}

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages(async statusContext =>
{
    var httpContext = statusContext.HttpContext;
    var response = httpContext.Response;
    var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
    _ = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
    {
        HttpContext = httpContext,
        ProblemDetails = new ProblemDetails
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
        }
    });
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
if (allowedOrigins.Length > 0)
{
    app.UseCors("TrustedWeb");
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = WriteHealthResponseAsync
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync
}).AllowAnonymous();
app.MapControllers();
app.Run();

static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    var message = report.Status == HealthStatus.Healthy
        ? "Uygulama hazır."
        : "Uygulama bağımlılıkları hazır değil.";
    return context.Response.WriteAsJsonAsync(
        new
        {
            status = report.Status.ToString(),
            correlationId = CorrelationIdMiddleware.Get(context),
            message
        },
        context.RequestAborted);
}

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
