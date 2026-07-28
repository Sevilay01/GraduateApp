using System.Diagnostics;
using GraduateApp.Web.Models;
using GraduateApp.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GraduateApp.Web.Controllers;

public sealed class HomeController(GraduateApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        int? academicYearStart,
        AcademicTerm? term,
        CancellationToken cancellationToken)
    {
        var programSearch = OpenProgramSearchViewModel.From(search, academicYearStart, term);
        if (!TryValidateModel(programSearch, nameof(HomeViewModel.ProgramSearch)))
        {
            return View(new HomeViewModel
            {
                ProgramSearch = programSearch
            });
        }

        var result = await apiClient.GetOpenProgramsAsync(
            programSearch.Search,
            programSearch.AcademicYearStart,
            programSearch.Term,
            cancellationToken);
        return View(new HomeViewModel
        {
            ProgramSearch = programSearch,
            OpenPrograms = result.Value ?? [],
            ErrorMessage = result.IsSuccess ? null : result.Error
        });
    }

    [NonAction]
    public Task<IActionResult> Index(CancellationToken cancellationToken) =>
        Index(null, null, null, cancellationToken);

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
