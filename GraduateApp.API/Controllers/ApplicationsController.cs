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

        // 1. ÖĞRENCİ İÇİN: Yeni Başvuru Yap
        [HttpPost]
        public async Task<IActionResult> CreateApplication([FromBody] ApplicationCreateDto dto)
        {
            var existingApp = await _context.Applications
                .FirstOrDefaultAsync(a => a.Tc == dto.Tc && a.ProgramId == dto.ProgramId);

            if (existingApp != null)
                return BadRequest("Bu programa zaten bir başvurunuz bulunmaktadır.");

            var newApplication = new Application
            {
                Tc = dto.Tc,
                ProgramId = dto.ProgramId,
                ApplicationDate = DateTime.Now,
                CurrentStatus = "Onay Bekliyor"
            };

            _context.Applications.Add(newApplication);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Başvurunuz başarıyla alındı!" });
        }

        // 2. ÖĞRENCİ İÇİN: Kendi Başvurularını Getir
        [HttpGet("student/{tc}")]
        public async Task<IActionResult> GetStudentApplications(string tc)
        {
            var applications = await _context.Applications
                .Include(a => a.Program)
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

        // 3. ADMIN İÇİN: Tüm Başvuruları Getir
        [HttpGet]
        public async Task<IActionResult> GetAllApplications()
        {
            // Öğrenci (TcNavigation) ve Program bilgilerini birlikte çekiyoruz
            var applications = await _context.Applications
                .Include(a => a.Program)
                .Include(a => a.TcNavigation)
                .Select(a => new {
                    a.ApplicationId,
                    a.Tc,
                    StudentFullName = a.TcNavigation.StudentName + " " + a.TcNavigation.StudentSurname,
                    a.ProgramId,
                    ProgramName = a.Program.ProgramName,
                    a.ApplicationDate,
                    a.CurrentStatus
                })
                .ToListAsync();

            return Ok(applications);
        }

        // 4. ADMIN İÇİN: Başvuru Durumunu Güncelle
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateApplicationStatus(int id, [FromBody] ApplicationStatusUpdateDto dto)
        {
            var application = await _context.Applications.FindAsync(id);

            if (application == null)
                return NotFound("Başvuru bulunamadı.");

            // Durumu güncelliyoruz
            application.CurrentStatus = dto.NewStatus;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Başvuru durumu başarıyla güncellendi." });
        }
    }
}