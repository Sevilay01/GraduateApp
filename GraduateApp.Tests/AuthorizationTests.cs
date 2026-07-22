using System.Reflection;
using GraduateApp.API.Controllers;
using GraduateApp.API.DTOs;
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
    public void Api_admin_account_management_requires_admin_role_and_has_no_delete_endpoint()
    {
        var authorize = typeof(AdminAccountsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize!.Roles);
        Assert.DoesNotContain(typeof(AdminAccountsController).GetMethods(), method =>
            method.GetCustomAttributes().OfType<Microsoft.AspNetCore.Mvc.HttpDeleteAttribute>().Any());
    }

    [Fact]
    public void Institute_and_program_management_require_admin_and_exclude_student_role()
    {
        var controllerTypes = new[]
        {
            typeof(AdminInstitutesController),
            typeof(AdminProgramsController)
        };

        foreach (var controllerType in controllerTypes)
        {
            var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorize);
            Assert.Equal("Admin", authorize!.Roles);
            Assert.DoesNotContain("Student", authorize.Roles, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ApiAdminStudentManagement_UsesPublicId_and_does_not_return_or_accept_raw_tc()
    {
        var contractTypes = new[]
        {
            typeof(AdminStudentListItemDto),
            typeof(AdminStudentDetailDto)
        };
        Assert.All(contractTypes, type =>
            Assert.DoesNotContain(type.GetProperties(), property =>
                string.Equals(property.Name, "Tc", StringComparison.OrdinalIgnoreCase)));

        var endpointNames = new[]
        {
            nameof(AdminStudentsController.GetDetail),
            nameof(AdminStudentsController.Deactivate),
            nameof(AdminStudentsController.Activate)
        };
        foreach (var endpointName in endpointNames)
        {
            var method = typeof(AdminStudentsController).GetMethod(endpointName);
            Assert.NotNull(method);
            Assert.Contains(method!.GetParameters(), parameter =>
                parameter.Name == "publicId" && parameter.ParameterType == typeof(Guid));
            Assert.DoesNotContain(method.GetParameters(), parameter =>
                parameter.Name is "tc" or "studentTc");
            var route = method.GetCustomAttributes()
                .OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>()
                .Single()
                .Template;
            Assert.Contains("{publicId:guid}", route, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WebStudentDeactivation_UsesPostAndAntiforgery()
    {
        var method = typeof(AdminController).GetMethod(nameof(AdminController.ConfirmDeactivateStudent));

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>());
        Assert.NotNull(method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void WebStudentActivation_UsesPostAndAntiforgery()
    {
        var method = typeof(AdminController).GetMethod(nameof(AdminController.ConfirmActivateStudent));

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>());
        Assert.NotNull(method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void Web_institute_and_program_mutations_use_post_and_antiforgery()
    {
        var methodNames = new[]
        {
            nameof(AdminController.SaveInstitute),
            nameof(AdminController.ActivateInstitute),
            nameof(AdminController.DeactivateInstitute),
            nameof(AdminController.ConfirmDeleteInstitute),
            nameof(AdminController.SaveProgram),
            nameof(AdminController.ActivateProgram),
            nameof(AdminController.DeactivateProgram),
            nameof(AdminController.ConfirmDeleteProgram)
        };

        foreach (var methodName in methodNames)
        {
            var method = typeof(AdminController).GetMethod(methodName);
            Assert.NotNull(method);
            Assert.NotNull(method!.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>());
            Assert.NotNull(method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>());
        }
    }
}
