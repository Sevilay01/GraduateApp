using System.Reflection;
using GraduateApp.API.Controllers;
using GraduateApp.Web.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace GraduateApp.Tests;

public sealed class AuthorizationTests
{
    [Fact]
    public void WebAdminPanel_RequiresAdminRole()
    {
        var authorize = typeof(AdminController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize!.Roles);
    }

    [Fact]
    public void ApiAdminApplicationEndpoint_RequiresAdminRole()
    {
        var method = typeof(ApplicationsController).GetMethod(nameof(ApplicationsController.GetForAdmin));
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize!.Roles);
    }

    [Fact]
    public void ApiStudentApplicationEndpoint_RequiresStudentRole()
    {
        var method = typeof(ApplicationsController).GetMethod(nameof(ApplicationsController.GetMine));
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Student", authorize!.Roles);
    }

    [Fact]
    public void ApiProgramOfferingManagement_RequiresAdminRole()
    {
        var authorize = typeof(ProgramOfferingsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize!.Roles);
    }
}
