using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GraduateApp.API.Models;

namespace GraduateApp.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ApplicationsController : ControllerBase
    {
        private readonly GraduateAppDbContext _context;

        public ApplicationsController(GraduateAppDbContext context)
        {
            _context = context;
        }

        // POST: api/applications (Yeni Başvuru Yap)
        [HttpPost]
        public async Task<IActionResult> CreateApplication([FromBody] ApplicationCreateDto dto)
        {
            // Aynı programa daha önce başvurmuş mu kontrolü
            var existingApp = await _context.Applications
                .FirstOrDefaultAsync(a => a.Tc == dto.Tc && a.ProgramId == dto.ProgramId);

            if (existingApp != null)
                return BadRequest("Bu programa zaten bir başvurunuz bulunmaktadır.");

            var newApplication = new Application
            {
                Tc = dto.Tc,
                ProgramId = dto.ProgramId,
                ApplicationDate = DateTime.Now,
                CurrentStatus = "Onay Bekliyor" // Senin orijinal property adın
            };

            _context.Applications.Add(newApplication);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Başvurunuz başarıyla alındı!" });
        }

        // GET: api/applications/student/{tc} (Öğrencinin Başvurularını Getir)
        [HttpGet("student/{tc}")]
        public async Task<IActionResult> GetStudentApplications(string tc)
        {
            var applications = await _context.Applications
                .Include(a => a.Program) // Program adını da çekmek için dahil ediyoruz
                .Where(a => a.Tc == tc)
                .Select(a => new {
                    a.ApplicationId,
                    a.ProgramId,
                    ProgramName = a.Program.ProgramName,
                    a.ApplicationDate,
                    a.CurrentStatus
                })
                .ToListAsync();

            return Ok(applications);
        }
    }
}