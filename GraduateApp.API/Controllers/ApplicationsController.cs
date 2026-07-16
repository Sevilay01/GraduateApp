using GraduateApp.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Controllers
{
    [Route("api/applications")]
    [ApiController]
    public class ApplicationsController : ControllerBase
    {
        private readonly GraduateAppDbContext _context;

        public ApplicationsController(GraduateAppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetApplications()
        {
            // Tabloları birleştirerek (JOIN) arayüze sadece gerekli ve okunabilir bilgileri yolluyoruz.
            var applications = await (from app in _context.Applications
                                      join std in _context.Students on app.Tc equals std.Tc
                                      join prg in _context.Programs on app.ProgramId equals prg.ProgramId
                                      select new
                                      {
                                          ApplicationID = app.ApplicationId,
                                          StudentFullName = std.StudentName + " " + std.StudentSurname,
                                          ProgramName = prg.ProgramName,
                                          ApplicationDate = app.ApplicationDate,
                                          CurrentStatus = app.CurrentStatus
                                      }).ToListAsync();

            return Ok(applications);
        }
        // POST: api/applications/5/status?newStatus=Onaylandı
        [HttpPost("{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromQuery] string newStatus)
        {
            // Önce veritabanından o ID'ye ait başvuruyu buluyoruz
            var application = await _context.Applications.FindAsync(id);

            if (application == null)
            {
                return NotFound(); // Başvuru yoksa 404 dön
            }

            // Durumu güncelleyip veritabanına kaydediyoruz
            application.CurrentStatus = newStatus;
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}