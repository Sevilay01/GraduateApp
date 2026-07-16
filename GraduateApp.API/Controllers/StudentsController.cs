using GraduateApp.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudentsController : ControllerBase
    {
        private readonly GraduateAppDbContext _context;

        // Veritabaný baðlantýmýzý (DbContext) içeri alýyoruz
        public StudentsController(GraduateAppDbContext context)
        {
            _context = context;
        }

        // GET: api/students
        [HttpGet]
        public async Task<IActionResult> GetStudents()
        {
            // Veritabanýndan Students tablosundaki tüm kayýtlarý çekiyoruz
            var students = await _context.Students.ToListAsync();

            return Ok(students); // 200 OK durumu ile veriyi JSON olarak döndür
        }
        // POST: api/students/login
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginData)
        {
            // Veritabanýnda bu TC ve Þifreye sahip bir öðrenci var mý diye bakýyoruz
            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Tc == loginData.TC && s.PasswordHash == loginData.Password);

            if (student == null)
            {
                // Eþleþme yoksa 401 Unauthorized (Yetkisiz) veya 400 Bad Request dönüyoruz
                return BadRequest("TC Kimlik No veya Þifre hatalý.");
            }

            // Eþleþme varsa adayýn temel bilgilerini (þifresi hariç) arayüze gönderiyoruz
            return Ok(new
            {
               
                student.Tc,
                student.StudentName,
                student.StudentSurname
            });
        }
        // POST: api/students
        [HttpPost]
        public async Task<IActionResult> PostStudent(Student student)
        {
            // Veritabaný kurallarýmýz gereði þifre boþ olamaz, þimdilik geçici bir þifre atýyoruz
            if (string.IsNullOrEmpty(student.PasswordHash))
            {
                student.PasswordHash = "gecici_hash_123456";
            }

            _context.Students.Add(student);
            await _context.SaveChangesAsync(); // Veritabanýna kaydet

            return Ok(student);
        }
    }
}