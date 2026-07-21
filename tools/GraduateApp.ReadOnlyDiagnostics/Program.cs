using GraduateApp.API.Models;
using GraduateApp.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var mode = Environment.GetEnvironmentVariable("GRADUATEAPP_DIAG_MODE") ?? "verify-live";
switch (mode)
{
    case "verify-live":
        await VerifyLiveAsync();
        break;
    case "create-clone":
        await CreateCloneAsync();
        break;
    case "inspect-clone":
        await InspectCloneAsync();
        break;
    case "set-test-password":
        await SetTestPasswordAsync();
        break;
    case "drop-clone":
        await DropCloneAsync();
        break;
    default:
        throw new InvalidOperationException("Unknown diagnostic mode.");
}

static async Task VerifyLiveAsync()
{
    var connectionString = Required("GRADUATEAPP_DIAG_CONNECTION");
    var email = Required("GRADUATEAPP_DIAG_EMAIL");
    var password = Required("GRADUATEAPP_DIAG_PASSWORD");
    var identity = await ReadAdminIdentityAsync(connectionString, email);
    Console.WriteLine($"IdentityCount={identity.Count}");
    Console.WriteLine($"IdentityConsistent={identity.IsConsistent}");
    if (identity.Admin is null)
    {
        return;
    }

    var verification = new PasswordHasher<Admin>()
        .VerifyHashedPassword(identity.Admin, identity.Admin.PasswordHash, password);
    Console.WriteLine($"BootstrapPasswordVerification={verification}");
}

static async Task CreateCloneAsync()
{
    var liveConnection = Required("GRADUATEAPP_DIAG_CONNECTION");
    var email = Required("GRADUATEAPP_DIAG_EMAIL");
    var databaseName = DiagnosticDatabaseName();
    var source = await ReadAdminIdentityAsync(liveConnection, email);
    if (source.Count != 1 || !source.IsConsistent || source.Admin is null)
    {
        throw new InvalidOperationException("Live identity is not safe to clone.");
    }

    await using (var master = new SqlConnection(LocalConnectionString("master")))
    {
        await master.OpenAsync();
        await using var create = master.CreateCommand();
        create.CommandText = $"CREATE DATABASE [{databaseName}];";
        await create.ExecuteNonQueryAsync();
    }

    try
    {
        await using var target = CreateDb(LocalConnectionString(databaseName));
        await target.Database.EnsureCreatedAsync();
        var now = DateTime.UtcNow;
        var admin = new Admin
        {
            Email = source.Admin.Email,
            NormalizedEmail = source.Admin.NormalizedEmail,
            PasswordHash = source.Admin.PasswordHash,
            SecurityStamp = source.Admin.SecurityStamp,
            AccessFailedCount = 0,
            LockoutEndUtc = null,
            MustChangePassword = source.Admin.MustChangePassword,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        admin.LoginIdentity = new LoginIdentity
        {
            NormalizedEmail = admin.NormalizedEmail,
            AccountType = LoginAccountType.Admin,
            Admin = admin,
            CreatedAtUtc = now
        };
        target.Admins.Add(admin);
        await target.SaveChangesAsync();
        Console.WriteLine("CloneCreated=True");
    }
    catch
    {
        await DropDatabaseAsync(databaseName);
        throw;
    }
}

static async Task InspectCloneAsync()
{
    await using var db = CreateDb(LocalConnectionString(DiagnosticDatabaseName()));
    var admin = await db.Admins.AsNoTracking().SingleAsync();
    Console.WriteLine("AdminCount=1");
    Console.WriteLine($"AccessFailedCount={admin.AccessFailedCount}");
    Console.WriteLine($"LockoutEndUtcIsNull={admin.LockoutEndUtc is null}");
}

static async Task SetTestPasswordAsync()
{
    var password = Required("GRADUATEAPP_DIAG_TEST_PASSWORD");
    var email = Required("GRADUATEAPP_DIAG_TEST_EMAIL");
    var normalizedEmail = new InvariantEmailNormalizer().Normalize(email);
    await using var db = CreateDb(LocalConnectionString(DiagnosticDatabaseName()));
    var admin = await db.Admins.Include(item => item.LoginIdentity).SingleAsync();
    admin.Email = email;
    admin.NormalizedEmail = normalizedEmail;
    admin.LoginIdentity!.NormalizedEmail = normalizedEmail;
    admin.PasswordHash = new PasswordHasher<Admin>().HashPassword(admin, password);
    admin.AccessFailedCount = 0;
    admin.LockoutEndUtc = null;
    admin.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    Console.WriteLine("TestPasswordSet=True");
}

static async Task DropCloneAsync() => await DropDatabaseAsync(DiagnosticDatabaseName());

static async Task<(int Count, bool IsConsistent, Admin? Admin)> ReadAdminIdentityAsync(
    string connectionString,
    string email)
{
    var normalizer = new InvariantEmailNormalizer();
    var normalizedEmail = normalizer.Normalize(email);
    await using var db = CreateDb(connectionString);
    var identities = await db.LoginIdentities
        .AsNoTracking()
        .Include(identity => identity.Admin)
        .Include(identity => identity.Student)
        .Where(identity => identity.AccountType == LoginAccountType.Admin
            && identity.NormalizedEmail == normalizedEmail)
        .OrderBy(identity => identity.LoginIdentityId)
        .Take(2)
        .ToListAsync();
    if (identities.Count != 1)
    {
        return (identities.Count, false, null);
    }

    var identity = identities[0];
    var consistent = identity.StudentTc is null
        && identity.AdminId is not null
        && identity.Student is null
        && identity.Admin is not null
        && string.Equals(
            identity.NormalizedEmail,
            normalizer.Normalize(identity.Admin.Email),
            StringComparison.Ordinal)
        && string.Equals(
            identity.Admin.NormalizedEmail,
            identity.NormalizedEmail,
            StringComparison.Ordinal);
    return (1, consistent, identity.Admin);
}

static GraduateAppDbContext CreateDb(string connectionString) => new(
    new DbContextOptionsBuilder<GraduateAppDbContext>()
        .UseSqlServer(connectionString)
        .Options);

static string LocalConnectionString(string databaseName) => new SqlConnectionStringBuilder
{
    DataSource = "(localdb)\\MSSQLLocalDB",
    InitialCatalog = databaseName,
    IntegratedSecurity = true,
    Encrypt = false,
    ConnectTimeout = 10
}.ConnectionString;

static string DiagnosticDatabaseName()
{
    var name = Required("GRADUATEAPP_DIAG_DATABASE");
    if (!name.StartsWith("GraduateAppAdminLoginDiagnostic_", StringComparison.Ordinal)
        || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
    {
        throw new InvalidOperationException("Unsafe diagnostic database name.");
    }

    return name;
}

static async Task DropDatabaseAsync(string databaseName)
{
    SqlConnection.ClearAllPools();
    await using var master = new SqlConnection(LocalConnectionString("master"));
    await master.OpenAsync();
    await using var drop = master.CreateCommand();
    drop.CommandText = $"""
        IF DB_ID(N'{databaseName}') IS NOT NULL
        BEGIN
            ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE [{databaseName}];
        END;
        """;
    await drop.ExecuteNonQueryAsync();
    Console.WriteLine("CloneDropped=True");
}

static string Required(string name) => Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Missing diagnostic setting: {name}.");
