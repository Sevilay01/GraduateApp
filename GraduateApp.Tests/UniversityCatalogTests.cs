using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using GraduateApp.API.Controllers;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using GraduateApp.API.Security;
using GraduateApp.API.Services;
using GraduateApp.Web.Controllers;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GraduateApp.Tests;

public sealed class UniversityCatalogTests
{
    private static readonly TimeSpan ApiRequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReadinessRequestTimeout = TimeSpan.FromSeconds(1);
    [Fact]
    public async Task Empty_catalog_returns_an_empty_list()
    {
        await using var db = TestDb.Create();

        var result = await CreateService(db).GetAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Admin_create_trims_name_exposes_it_to_students_and_writes_one_safe_audit()
    {
        await using var db = TestDb.Create();
        var created = await CreateService(db).CreateAsync(
            42,
            new UniversityCreateDto { UniversityName = "  Test Üniversitesi  " },
            CancellationToken.None);

        var studentCatalog = await new StudentProfileService(
                db,
                TimeProvider.System,
                new InvariantEmailNormalizer())
            .GetUniversitiesAsync(CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal("Test Üniversitesi", Assert.Single(studentCatalog).UniversityName);
        var audit = Assert.Single(db.SecurityAuditLogs);
        Assert.Equal("UniversityCreated", audit.EventType);
        Assert.Equal("University", audit.TargetType);
        Assert.Equal(42, audit.ActorAdminId);
        Assert.Equal(created.Value!.UniversityId.ToString(), audit.TargetId);
        Assert.DoesNotContain("Test Üniversitesi", audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_is_ordered_by_name_then_id()
    {
        await using var db = TestDb.Create();
        db.Universities.AddRange(
            new University { UniversityName = "Z Üniversitesi" },
            new University { UniversityName = "A Üniversitesi" },
            new University { UniversityName = "A Üniversitesi" });
        await db.SaveChangesAsync();
        var expected = db.Universities
            .OrderBy(item => item.UniversityName)
            .ThenBy(item => item.UniversityId)
            .Select(item => item.UniversityId)
            .ToArray();

        var result = await CreateService(db).GetAsync(CancellationToken.None);

        Assert.Equal(expected, result.Select(item => item.UniversityId));
    }

    [Fact]
    public async Task Invalid_names_are_rejected_without_rows_or_success_audits()
    {
        var invalidNames = new[]
        {
            string.Empty,
            " ",
            "A",
            new string('A', 101),
            "Test\nÜniversitesi",
            "Test\u0000Üniversitesi"
        };

        foreach (var name in invalidNames)
        {
            await using var db = TestDb.Create();
            var result = await CreateService(db).CreateAsync(
                7,
                new UniversityCreateDto { UniversityName = name },
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
            Assert.Empty(db.Universities);
            Assert.Empty(db.SecurityAuditLogs);
        }
    }

    [Fact]
    public async Task Exact_duplicate_returns_conflict_without_a_second_audit()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db);
        var first = await service.CreateAsync(
            7,
            new UniversityCreateDto { UniversityName = "Ankara Üniversitesi" },
            CancellationToken.None);
        var duplicate = await service.CreateAsync(
            7,
            new UniversityCreateDto { UniversityName = "  Ankara Üniversitesi  " },
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, duplicate.StatusCode);
        Assert.Single(db.Universities);
        Assert.Single(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task Non_unique_database_failure_is_not_reported_as_a_duplicate()
    {
        await using var db = TestDb.Create(new ThrowingSaveChangesInterceptor(
            () => new DbUpdateException("non-unique database failure")));

        var result = await CreateService(db).CreateAsync(
            7,
            new UniversityCreateDto { UniversityName = "Test Üniversitesi" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.DoesNotContain("Aynı adda", result.Error, StringComparison.Ordinal);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public async Task Only_sql_server_unique_violations_are_mapped_to_safe_conflict(int errorNumber)
    {
        var sqlException = TestSqlExceptionFactory.Create(errorNumber);
        await using var db = TestDb.Create(new ThrowingSaveChangesInterceptor(
            () => new DbUpdateException("unique database failure", sqlException)));

        var result = await CreateService(db).CreateAsync(
            7,
            new UniversityCreateDto { UniversityName = "Test Üniversitesi" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal("Aynı adda bir üniversite zaten bulunuyor.", result.Error);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Theory]
    [InlineData(10928)]
    [InlineData(10929)]
    [InlineData(40143)]
    [InlineData(40197)]
    [InlineData(40501)]
    [InlineData(40540)]
    [InlineData(40613)]
    [InlineData(49918)]
    [InlineData(49919)]
    [InlineData(49920)]
    public async Task Azure_sql_unavailability_is_not_converted_to_a_service_conflict(int errorNumber)
    {
        var sqlException = TestSqlExceptionFactory.Create(errorNumber);
        await using var db = TestDb.Create(new ThrowingSaveChangesInterceptor(
            () => new DbUpdateException("database unavailable", sqlException)));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateService(db).CreateAsync(
                7,
                new UniversityCreateDto { UniversityName = "Test Üniversitesi" },
                CancellationToken.None));

        Assert.Same(sqlException, exception.InnerException);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task Mixed_unique_and_unavailable_errors_are_not_converted_to_conflict()
    {
        var sqlException = TestSqlExceptionFactory.Create(2601, 40197);
        await using var db = TestDb.Create(new ThrowingSaveChangesInterceptor(
            () => new DbUpdateException("mixed database failure", sqlException)));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            CreateService(db).CreateAsync(
                7,
                new UniversityCreateDto { UniversityName = "Test Üniversitesi" },
                CancellationToken.None));

        Assert.Same(sqlException, exception.InnerException);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [LocalDbFact]
    public async Task Audit_failure_rolls_back_the_university_in_the_same_transaction()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppUniversityAuditAtomicity_{Guid.NewGuid():N}",
            databaseCollation: null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var interceptor = new FailOnSaveNumberInterceptor(2);
        var interceptedOptions = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        await using (var db = new GraduateAppDbContext(interceptedOptions))
        {
            var result = await CreateService(db).CreateAsync(
                7,
                new UniversityCreateDto { UniversityName = "Atomik Üniversite" },
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        }

        var verificationOptions = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;
        await using var verification = new GraduateAppDbContext(verificationOptions);
        Assert.Empty(await verification.Universities.AsNoTracking().ToListAsync());
        Assert.Empty(await verification.SecurityAuditLogs.AsNoTracking().ToListAsync());
    }

    [LocalDbFact]
    public async Task Collation_duplicate_and_concurrent_create_leave_one_row_and_one_audit()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppUniversityCatalog_{Guid.NewGuid():N}",
            "Turkish_100_CI_AS");
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
            .UseSqlServer(database.ConnectionString)
            .Options;

        await using (var firstContext = new GraduateAppDbContext(options))
        {
            var first = await CreateService(firstContext).CreateAsync(
                11,
                new UniversityCreateDto { UniversityName = "İstanbul Üniversitesi" },
                CancellationToken.None);
            Assert.True(first.IsSuccess);
        }

        await using (var duplicateContext = new GraduateAppDbContext(options))
        {
            var collationDuplicate = await CreateService(duplicateContext).CreateAsync(
                11,
                new UniversityCreateDto { UniversityName = "  istanbul üniversitesi  " },
                CancellationToken.None);
            Assert.False(collationDuplicate.IsSuccess);
            Assert.Equal(StatusCodes.Status409Conflict, collationDuplicate.StatusCode);
        }

        var raceName = $"Yarış Üniversitesi {Guid.NewGuid():N}";
        await using var raceContextOne = new GraduateAppDbContext(options);
        await using var raceContextTwo = new GraduateAppDbContext(options);
        var attempts = await Task.WhenAll(
            CreateService(raceContextOne).CreateAsync(
                21,
                new UniversityCreateDto { UniversityName = raceName },
                CancellationToken.None),
            CreateService(raceContextTwo).CreateAsync(
                22,
                new UniversityCreateDto { UniversityName = raceName },
                CancellationToken.None));

        Assert.Equal(1, attempts.Count(item => item.IsSuccess));
        Assert.Equal(
            StatusCodes.Status409Conflict,
            Assert.Single(attempts, item => !item.IsSuccess).StatusCode);
        await using var verification = new GraduateAppDbContext(options);
        var racedUniversity = await verification.Universities.AsNoTracking()
            .SingleAsync(item => item.UniversityName == raceName);
        Assert.Equal(1, await verification.SecurityAuditLogs.CountAsync(
            item => item.EventType == "UniversityCreated"
                && item.TargetId == racedUniversity.UniversityId.ToString()));
    }

    [Fact]
    public void Api_and_web_mutation_boundaries_are_admin_only_post_antiforgery_and_create_only()
    {
        var authorize = typeof(AdminUniversitiesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(ApiAuthenticationDefaults.AdminRole, authorize!.Roles);
        Assert.DoesNotContain(ApiAuthenticationDefaults.StudentRole, authorize.Roles, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(AdminUniversitiesController).GetMethods(), method =>
            method.GetCustomAttributes().OfType<HttpDeleteAttribute>().Any()
            || method.GetCustomAttributes().OfType<HttpPutAttribute>().Any()
            || method.GetCustomAttributes().OfType<HttpPatchAttribute>().Any());

        var webMutation = typeof(AdminController).GetMethod(nameof(AdminController.CreateUniversity));
        Assert.NotNull(webMutation);
        Assert.NotNull(webMutation!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(webMutation.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [LocalDbFact]
    public async Task Api_enforces_401_and_403_then_allows_admin_create_visible_to_students()
    {
        await using var database = new LocalDbTestDatabase(
            $"GraduateAppUniversityAuthorization_{Guid.NewGuid():N}",
            databaseCollation: null);
        await database.CreateAsync();
        await database.CreateCurrentModelSchemaAsync();
        var port = GetFreeTcpPort();
        var adminPassword = $"Admin-{Guid.NewGuid():N}-aA1!";
        const string adminEmail = "catalog.admin@local.test";
        using var process = StartApiProcess(
            database.ConnectionString,
            port,
            adminEmail,
            adminPassword);
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = ApiRequestTimeout
        };

        try
        {
            await WaitUntilReadyAsync(client, process);
            using (var unauthenticated = await client.PostAsJsonAsync(
                "api/admin/universities",
                new { universityName = "Yetkisiz Üniversite" }))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            }

            var password = $"Student-{Guid.NewGuid():N}-aA1!";
            using (var registration = await client.PostAsJsonAsync("api/auth/register", new
            {
                tc = "10000000146",
                firstName = "Test",
                lastName = "Student",
                fatherName = "Test Parent",
                birthDate = new DateOnly(2000, 1, 1),
                email = "authorization.student@local.test",
                telephone = "05000000146",
                password,
                confirmPassword = password
            }))
            {
                Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
            }

            using var login = await client.PostAsJsonAsync("api/auth/login", new
            {
                username = "authorization.student@local.test",
                password,
                accountType = "Student"
            });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var loginResponse = await login.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(loginResponse);
            var studentToken = loginResponse!.AccessToken;
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", studentToken);
            using var studentAttempt = await client.PostAsJsonAsync(
                "api/admin/universities",
                new { universityName = "Öğrenci Üniversitesi" });
            Assert.Equal(HttpStatusCode.Forbidden, studentAttempt.StatusCode);

            using var adminLogin = await client.PostAsJsonAsync("api/auth/login", new
            {
                username = adminEmail,
                password = adminPassword,
                accountType = "Admin"
            });
            Assert.Equal(HttpStatusCode.OK, adminLogin.StatusCode);
            var adminLoginResponse = await adminLogin.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(adminLoginResponse);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", adminLoginResponse!.AccessToken);
            using var adminCreate = await client.PostAsJsonAsync(
                "api/admin/universities",
                new { universityName = "API Üniversitesi" });
            Assert.Equal(HttpStatusCode.Created, adminCreate.StatusCode);

            var adminCatalog = await client.GetFromJsonAsync<IReadOnlyList<UniversityDto>>(
                "api/admin/universities");
            Assert.Equal("API Üniversitesi", Assert.Single(adminCatalog!).UniversityName);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", studentToken);
            var studentCatalog = await client.GetFromJsonAsync<IReadOnlyList<UniversityDto>>(
                "api/students/universities");
            Assert.Equal("API Üniversitesi", Assert.Single(studentCatalog!).UniversityName);

            var options = new DbContextOptionsBuilder<GraduateAppDbContext>()
                .UseSqlServer(database.ConnectionString)
                .Options;
            await using var verification = new GraduateAppDbContext(options);
            Assert.Single(await verification.Universities.AsNoTracking().ToListAsync());
            Assert.Single(await verification.SecurityAuditLogs
                .AsNoTracking()
                .Where(item => item.EventType == "UniversityCreated")
                .ToListAsync());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [Fact]
    public async Task Profile_controller_distinguishes_failure_from_empty_and_preserves_existing_selection()
    {
        var emptyController = CreatePanelController(new ProfileHandler(
            universitiesStatus: HttpStatusCode.OK,
            universitiesJson: "[]"));
        var emptyResult = Assert.IsType<ViewResult>(
            await emptyController.Profile(CancellationToken.None));
        var emptyModel = Assert.IsType<StudentProfileViewModel>(emptyResult.Model);
        Assert.True(emptyModel.UniversityCatalogLoadSucceeded);
        Assert.Empty(emptyModel.Universities);

        var failureController = CreatePanelController(new ProfileHandler(
            universitiesStatus: HttpStatusCode.InternalServerError,
            universitiesJson: """{"detail":"Katalog servisi kullanılamıyor."}"""));
        var failureResult = Assert.IsType<ViewResult>(
            await failureController.Profile(CancellationToken.None));
        var failureModel = Assert.IsType<StudentProfileViewModel>(failureResult.Model);
        Assert.False(failureModel.UniversityCatalogLoadSucceeded);
        Assert.Equal("Katalog servisi kullanılamıyor.", failureModel.UniversityCatalogErrorMessage);

        var populatedController = CreatePanelController(new ProfileHandler(
            universitiesStatus: HttpStatusCode.OK,
            universitiesJson: """[{"universityId":7,"universityName":"Test Üniversitesi"}]"""));
        var populatedResult = Assert.IsType<ViewResult>(
            await populatedController.Profile(CancellationToken.None));
        var populatedModel = Assert.IsType<StudentProfileViewModel>(populatedResult.Model);
        Assert.Equal(7, populatedModel.UniversityId);
        Assert.Equal(7, Assert.Single(populatedModel.Universities).UniversityId);
    }

    [Fact]
    public async Task Real_profile_and_admin_views_render_catalog_states_selection_and_encoded_names()
    {
        using var host = CreateWebHost();
        const string unsafeName = "<script>alert('catalog')</script>";

        var emptyHtml = await RenderViewAsync(
            host.Services,
            "Panel",
            "Profile",
            new StudentProfileViewModel
            {
                UniversityCatalogLoadSucceeded = true,
                Universities = []
            });
        Assert.Contains(
            "Üniversite kataloğu henüz tanımlanmamış. Lütfen yöneticiyle iletişime geçin.",
            WebUtility.HtmlDecode(emptyHtml),
            StringComparison.Ordinal);
        Assert.Contains("disabled=\"disabled\"", emptyHtml, StringComparison.Ordinal);

        var failureHtml = await RenderViewAsync(
            host.Services,
            "Panel",
            "Profile",
            new StudentProfileViewModel
            {
                UniversityCatalogLoadSucceeded = false,
                UniversityCatalogErrorMessage = "Kontrollü katalog hatası.",
                Universities = []
            });
        var decodedFailure = WebUtility.HtmlDecode(failureHtml);
        Assert.Contains("Üniversite kataloğu yüklenemedi.", decodedFailure, StringComparison.Ordinal);
        Assert.Contains("Kontrollü katalog hatası.", decodedFailure, StringComparison.Ordinal);
        Assert.DoesNotContain("henüz tanımlanmamış", decodedFailure, StringComparison.Ordinal);

        var populatedHtml = await RenderViewAsync(
            host.Services,
            "Panel",
            "Profile",
            new StudentProfileViewModel
            {
                UniversityId = 7,
                UniversityCatalogLoadSucceeded = true,
                Universities =
                [
                    new UniversityViewModel
                    {
                        UniversityId = 7,
                        UniversityName = unsafeName
                    }
                ]
            });
        Assert.DoesNotContain("<script>", populatedHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", populatedHtml, StringComparison.Ordinal);
        var optionTag = OpeningTagContaining(populatedHtml, "&lt;script&gt;", "option");
        Assert.Contains("selected", optionTag, StringComparison.OrdinalIgnoreCase);
        var gnoInput = OpeningTagContaining(populatedHtml, "name=\"Gno\"", "input");
        Assert.DoesNotContain("disabled", gnoInput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Değişiklikler mevcut başvuruların değerlendirme verilerini etkilemez.",
            WebUtility.HtmlDecode(populatedHtml),
            StringComparison.Ordinal);

        var adminHtml = await RenderViewAsync(
            host.Services,
            "Admin",
            "Universities",
            new UniversityPageViewModel
            {
                Universities =
                [
                    new UniversityViewModel
                    {
                        UniversityId = 7,
                        UniversityName = unsafeName
                    }
                ]
            });
        Assert.DoesNotContain("<script>", adminHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", adminHtml, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", adminHtml, StringComparison.Ordinal);
    }

    private static UniversityCatalogService CreateService(GraduateAppDbContext db) =>
        new(db, new TestTimeProvider(new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.Zero)));

    private static PanelController CreatePanelController(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        return new PanelController(new GraduateApiClient(client));
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
                        .AddApplicationPart(typeof(AdminController).Assembly);
                })
                .Configure(_ => { }))
            .Build();

    private static async Task<string> RenderViewAsync<TModel>(
        IServiceProvider services,
        string controller,
        string view,
        TModel model)
    {
        using var scope = services.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var httpContext = new DefaultHttpContext { RequestServices = scopedServices };
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost");
        var routeData = new RouteData();
        routeData.Values["controller"] = controller;
        routeData.Values["action"] = view;
        routeData.Routers.Add(new TestRouter());
        var actionContext = new ActionContext(
            httpContext,
            routeData,
            new ActionDescriptor(),
            new ModelStateDictionary());
        var viewEngine = scopedServices.GetRequiredService<ICompositeViewEngine>();
        var viewResult = viewEngine.FindView(actionContext, view, isMainPage: false);
        Assert.True(
            viewResult.Success,
            $"{controller}/{view} view bulunamadı: {string.Join(", ", viewResult.SearchedLocations ?? [])}");
        var viewData = new ViewDataDictionary<TModel>(
            scopedServices.GetRequiredService<Microsoft.AspNetCore.Mvc.ModelBinding.IModelMetadataProvider>(),
            actionContext.ModelState)
        {
            Model = model
        };
        var tempData = new TempDataDictionary(
            httpContext,
            scopedServices.GetRequiredService<ITempDataProvider>());
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
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

    private static string OpeningTagContaining(string html, string marker, string tagName)
    {
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Element marker bulunamadı: {marker}");
        var tagStart = html.LastIndexOf($"<{tagName}", markerIndex, StringComparison.Ordinal);
        var tagEnd = html.IndexOf('>', tagStart);
        Assert.True(tagStart >= 0 && tagEnd > tagStart);
        return html[tagStart..(tagEnd + 1)];
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static Process StartApiProcess(
        string connectionString,
        int port,
        string bootstrapAdminEmail,
        string bootstrapAdminPassword)
    {
        var repositoryRoot = RepositoryRoot();
        var apiProjectDirectory = Path.Combine(repositoryRoot, "GraduateApp.API");
        var apiAssembly = Path.Combine(
            apiProjectDirectory,
            "bin", "Release", "net10.0", "GraduateApp.API.dll");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = apiProjectDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(apiAssembly);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add($"http://127.0.0.1:{port}");
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = connectionString;
        startInfo.Environment["Logging__LogLevel__Default"] = "Warning";
        startInfo.Environment["BootstrapAdmin__Email"] = bootstrapAdminEmail;
        startInfo.Environment["BootstrapAdmin__Password"] = bootstrapAdminPassword;
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Isolated API process could not be started.");
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        return process;
    }

    private static async Task WaitUntilReadyAsync(HttpClient client, Process process)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException("Isolated API process exited before readiness.");
            }

            try
            {
                using var attemptCancellation = new CancellationTokenSource(
                    ReadinessRequestTimeout);
                using var response = await client.GetAsync(
                    "diagnostic-readiness",
                    attemptCancellation.Token);
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                await Task.Delay(100);
            }
        }

        throw new TimeoutException("Isolated API process did not become ready.");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class ProfileHandler(
        HttpStatusCode universitiesStatus,
        string universitiesJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/api/students/me", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "tcMasked":"*******0146",
                          "firstName":"Test",
                          "lastName":"Student",
                          "email":"student@local.test",
                          "education":{
                            "universityId":7,
                            "faculty":"Test Fakültesi",
                            "graduatedProgram":"Test Programı",
                            "gno":3.5
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                });
            }

            Assert.EndsWith("/api/students/universities", path, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(universitiesStatus)
            {
                Content = new StringContent(universitiesJson, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FailOnSaveNumberInterceptor(int failureSaveNumber) : SaveChangesInterceptor
    {
        private int saveNumber;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            saveNumber++;
            return saveNumber == failureSaveNumber
                ? ValueTask.FromException<InterceptionResult<int>>(
                    new DbUpdateException("non-unique audit persistence failure"))
                : base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class TestRouter : IRouter
    {
        public Task RouteAsync(RouteContext context) => Task.CompletedTask;

        public VirtualPathData GetVirtualPath(VirtualPathContext context) =>
            new(this, $"/{context.Values["controller"]}/{context.Values["action"]}");
    }
}
