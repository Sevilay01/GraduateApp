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

    [Fact]
    public void ApiStudentExamScoreEndpoints_RequireStudentRole_AndDoNotAcceptTc()
    {
        var authorize = typeof(StudentsController).GetCustomAttribute<AuthorizeAttribute>();
        var methods = new[]
        {
            nameof(StudentsController.GetExamScores),
            nameof(StudentsController.GetExamCatalog),
            nameof(StudentsController.CreateExamScore),
            nameof(StudentsController.UpdateExamScore),
            nameof(StudentsController.DeleteExamScore)
        };

        Assert.NotNull(authorize);
        Assert.Equal("Student", authorize!.Roles);
        foreach (var methodName in methods)
        {
            var method = typeof(StudentsController).GetMethod(methodName);
            Assert.NotNull(method);
            Assert.DoesNotContain(method!.GetParameters(), parameter =>
                parameter.Name is "tc" or "studentTc");
        }
    }

    [Fact]
    public void ApiAdminStudentManagement_RequiresAdminRole()
    {
        var authorize = typeof(AdminStudentsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize!.Roles);
    }

    [Fact]
    public void WebStudentDeactivation_UsesPostAndAntiforgery()
    {
        var method = typeof(AdminController).GetMethod(nameof(AdminController.ConfirmDeactivateStudent));

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>());
        Assert.NotNull(method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>());
    }
}
