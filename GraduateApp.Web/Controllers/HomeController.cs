using System.Diagnostics;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Web.Controllers;

public sealed class HomeController(GraduateApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var result = await apiClient.GetOpenProgramsAsync(cancellationToken);
        return View(new HomeViewModel
        {
            OpenPrograms = result.Value ?? [],
            ErrorMessage = result.IsSuccess ? null : result.Error
        });
    }

    [HttpGet]
    public IActionResult Privacy() => View();

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
    });

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult HttpStatus(int code)
    {
        Response.StatusCode = code;
        ViewData["StatusCode"] = code;
        return View("StatusCode");
    }
}
