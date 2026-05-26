using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;
using StarComplexAPI.Models;

namespace StarComplexAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UploadsController : ControllerBase
    {
        private readonly StarComplexContext _context;
        private readonly IWebHostEnvironment _environment;

        public UploadsController(StarComplexContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // 1. رفع صور عقود الساكنين (جدول residents)
        [HttpPost("UploadResidentContract/{residentId}")]
        public async Task<IActionResult> UploadResidentContract(int residentId, IFormFile file)
        {
            var resident = await _context.Residents.FindAsync(residentId);
            if (resident == null) return NotFound("الساكن غير موجود.");

            string folderName = "Contracts";
            // التسمية: Contract_Unit101_[Guid].jpg
            string fileName = $"Contract_Unit{resident.unit_id}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

            resident.contract_path_1 = await SaveFile(file, folderName, fileName);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "تم رفع العقد بنجاح", Path = resident.contract_path_1 });
        }

        // 2. رفع هويات أفراد العائلة (جدول family_members)
        [HttpPost("UploadFamilyMemberId/{memberId}")]
        public async Task<IActionResult> UploadFamilyMemberId(int memberId, IFormFile file)
        {
            var member = await _context.FamilyMembers.FindAsync(memberId);
            if (member == null) return NotFound("فرد العائلة غير موجود.");

            string folderName = "FamilyIds";
            // التسمية: Family_Member55_[Guid].png
            string fileName = $"Family_Member{memberId}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

            member.national_id_front_path = await SaveFile(file, folderName, fileName);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "تم رفع هوية فرد العائلة", Path = member.national_id_front_path });
        }

        // 3. رفع هويات الموظفين (جدول employees)
        [HttpPost("UploadEmployeeId/{employeeId}")]
        public async Task<IActionResult> UploadEmployeeId(int employeeId, IFormFile file)
        {
            var employee = await _context.Employees.FindAsync(employeeId);
            if (employee == null) return NotFound("الموظف غير موجود.");

            string folderName = "EmployeeIds";
            // التسمية: Employee_Emp001_[Guid].jpg
            string fileName = $"Employee_Emp{employee.employee_code}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

            employee.national_id_front_path = await SaveFile(file, folderName, fileName);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "تم رفع هوية الموظف", Path = employee.national_id_front_path });
        }

        // دالة مساعدة لحفظ الملف في المجلد المخصص
        private async Task<string> SaveFile(IFormFile file, string folderName, string fileName)
        {
            if (file == null || file.Length == 0) throw new Exception("الملف غير صالح.");

            string wwwRootPath = _environment.WebRootPath;
            string path = Path.Combine(wwwRootPath, folderName);

            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            string fullPath = Path.Combine(path, fileName);
            using (var fileStream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            // نرجع المسار النسبي لحفظه في قاعدة البيانات
            return Path.Combine(folderName, fileName).Replace("\\", "/");
        }
    }
}