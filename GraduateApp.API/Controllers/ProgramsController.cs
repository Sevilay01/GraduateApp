using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GraduateApp.API.Models; // Kendi DB model namespace'ini kullan

namespace GraduateApp.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProgramsController : ControllerBase
    {
        private readonly GraduateAppDbContext _context;

        public ProgramsController(GraduateAppDbContext context)
        {
            _context = context;
        }

        // GET: api/programs
        [HttpGet]
        public async Task<IActionResult> GetPrograms()
        {
            // Sadece başvuru için gereken temel bilgileri arayüze dönüyoruz
            var programs = await _context.Programs
                .Select(p => new { 
                    p.ProgramId, 
                    p.ProgramName, 
                    p.DegreeType 
                })
                .ToListAsync();

            return Ok(programs);
        }
    }
}