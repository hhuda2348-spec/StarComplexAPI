using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;
using StarComplexAPI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StarComplexAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ResidentProfileController : ControllerBase
    {
        private readonly StarComplexContext _context;
        private readonly IWebHostEnvironment _environment;

        // ─── ثوابت الأمان ───
        private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        private static readonly string[] AllowedMimeTypes = { "image/jpeg", "image/png", "image/webp" };

        public ResidentProfileController(StarComplexContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // ══════════════════════════════════════════════════════
        // 1. جلب البروفايل
        // ══════════════════════════════════════════════════════
        [HttpGet("GetProfile/{unitId}")]
        public async Task<IActionResult> GetProfile(int unitId)
        {
            if (unitId <= 0) return BadRequest(new { message = "رقم الوحدة غير صحيح" });

            var resident = await _context.Residents
                .FirstOrDefaultAsync(r => r.unit_id == unitId);

            if (resident == null)
                return NotFound(new { message = "الساكن غير موجود" });

            return Ok(new
            {
                resident_id = resident.resident_id,
                unit_id = resident.unit_id,
                resident_code = resident.resident_code,          // ✅ كان مفقوداً
                first_name = resident.first_name,
                second_name = resident.second_name,
                third_name = resident.third_name,
                full_name = resident.FullName,               // ✅ مريح للـ client
                phone_number = resident.phone_number,
                resident_type = resident.resident_type,
                family_members_count = resident.family_members_count ?? 0,
                contract_path_1 = resident.contract_path_1,
                contract_path_2 = resident.contract_path_2
            });
        }

        // ══════════════════════════════════════════════════════
        // 2. تحديث بيانات الساكن وصور العقد
        // ══════════════════════════════════════════════════════
        [HttpPost("UpdatePartial")]
        public async Task<IActionResult> UpdatePartial([FromForm] UpdateResidentDto data)
        {
            if (data.UnitId <= 0)
                return BadRequest(new { message = "رقم الوحدة غير صحيح" });

            var resident = await _context.Residents
                .FirstOrDefaultAsync(r => r.unit_id == data.UnitId);

            if (resident == null) return NotFound(new { message = "الساكن غير موجود" });

            // ── التحقق من الصور قبل الحفظ ──
            if (data.ContractFront != null)
            {
                var validation = ValidateImage(data.ContractFront);
                if (validation != null) return BadRequest(new { message = validation });
            }
            if (data.ContractBack != null)
            {
                var validation = ValidateImage(data.ContractBack);
                if (validation != null) return BadRequest(new { message = validation });
            }

            try
            {
                string folder = Path.Combine(_environment.WebRootPath, "uploads", "contracts");
                Directory.CreateDirectory(folder); // آمن: لا يُنشئ إذا كان موجوداً

                if (data.ContractFront != null)
                {
                    // حذف الملف القديم إن وُجد
                    DeleteOldFile("uploads/contracts", resident.contract_path_1);

                    string ext = Path.GetExtension(data.ContractFront.FileName).ToLowerInvariant();
                    string fileName = $"res_{resident.resident_id}_c1{ext}";
                    await SaveFileAsync(data.ContractFront, Path.Combine(folder, fileName));
                    resident.contract_path_1 = fileName;
                }

                if (data.ContractBack != null)
                {
                    DeleteOldFile("uploads/contracts", resident.contract_path_2);

                    string ext = Path.GetExtension(data.ContractBack.FileName).ToLowerInvariant();
                    string fileName = $"res_{resident.resident_id}_c2{ext}";
                    await SaveFileAsync(data.ContractBack, Path.Combine(folder, fileName));
                    resident.contract_path_2 = fileName;
                }

                if (!string.IsNullOrWhiteSpace(data.PhoneNumber))
                    resident.phone_number = data.PhoneNumber.Trim();

                if (!string.IsNullOrWhiteSpace(data.ResidentType))
                    resident.resident_type = data.ResidentType.Trim();

                if (data.FamilyCount >= 1)
                    resident.family_members_count = data.FamilyCount;

                await _context.SaveChangesAsync();
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "حدث خطأ أثناء الحفظ", detail = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════
        // 3. إضافة أو تحديث فرد عائلة
        // ══════════════════════════════════════════════════════
        [HttpPost("AddFamilyMember")]
        public async Task<IActionResult> AddFamilyMember([FromForm] FamilyMemberUploadDto data)
        {
            if (data.UnitId <= 0)
                return BadRequest(new { message = "رقم الوحدة غير صحيح" });

            if (string.IsNullOrWhiteSpace(data.MemberName))
                return BadRequest(new { message = "اسم الفرد مطلوب" });

            if (data.FrontPhoto != null)
            {
                var v = ValidateImage(data.FrontPhoto);
                if (v != null) return BadRequest(new { message = v });
            }
            if (data.BackPhoto != null)
            {
                var v = ValidateImage(data.BackPhoto);
                if (v != null) return BadRequest(new { message = v });
            }

            var resident = await _context.Residents
                .Include(r => r.FamilyMembers)
                .FirstOrDefaultAsync(r => r.unit_id == data.UnitId);

            if (resident == null)
                return NotFound(new { message = "الساكن غير موجود" });

            // التحقق أن الـ index لا يتجاوز العدد المسموح به
            int maxAllowed = resident.family_members_count ?? 1;
            if (data.MemberIndex < 0 || data.MemberIndex >= maxAllowed)
                return BadRequest(new { message = "رقم الفرد غير صحيح" });

            try
            {
                string uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "ids");
                Directory.CreateDirectory(uploadsFolder);

                // ✅ الترتيب بـ member_id الصحيح (وليس first_name)
                var existingMember = await _context.FamilyMembers
                    .Where(m => m.resident_id == resident.resident_id)
                    .OrderBy(m => m.member_id)
                    .Skip(data.MemberIndex)
                    .FirstOrDefaultAsync();

                string prefix = data.MemberIndex == 0 ? "main" : $"mem_{data.MemberIndex}";
                string frontPath = null;
                string backPath = null;

                if (data.FrontPhoto != null)
                {
                    string ext = Path.GetExtension(data.FrontPhoto.FileName).ToLowerInvariant();
                    frontPath = $"{prefix}_{resident.resident_id}_front{ext}";
                    // حذف القديم
                    if (existingMember != null)
                        DeleteOldFile("uploads/ids", existingMember.national_id_front_path);
                    await SaveFileAsync(data.FrontPhoto, Path.Combine(uploadsFolder, frontPath));
                }

                if (data.BackPhoto != null)
                {
                    string ext = Path.GetExtension(data.BackPhoto.FileName).ToLowerInvariant();
                    backPath = $"{prefix}_{resident.resident_id}_back{ext}";
                    if (existingMember != null)
                        DeleteOldFile("uploads/ids", existingMember.national_id_back_path);
                    await SaveFileAsync(data.BackPhoto, Path.Combine(uploadsFolder, backPath));
                }

                var names = (data.MemberName ?? "")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (existingMember != null)
                {
                    existingMember.first_name = names.Length > 0 ? names[0] : existingMember.first_name;
                    existingMember.second_name = names.Length > 1 ? names[1] : "";
                    existingMember.third_name = names.Length > 2
                        ? string.Join(" ", names.Skip(2)) : "";

                    if (frontPath != null) existingMember.national_id_front_path = frontPath;
                    if (backPath != null) existingMember.national_id_back_path = backPath;
                }
                else
                {
                    var newMember = new FamilyMember
                    {
                        resident_id = resident.resident_id,
                        first_name = names.Length > 0 ? names[0] : data.MemberName,
                        second_name = names.Length > 1 ? names[1] : "",
                        third_name = names.Length > 2
                            ? string.Join(" ", names.Skip(2)) : "",
                        national_id_front_path = frontPath,
                        national_id_back_path = backPath
                    };
                    _context.FamilyMembers.Add(newMember);
                }

                await _context.SaveChangesAsync();
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "حدث خطأ أثناء الحفظ", detail = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════
        // 4. جلب قائمة أفراد العائلة
        // ══════════════════════════════════════════════════════
        [HttpGet("GetFamily/{unitId}")]
        public async Task<IActionResult> GetFamily(int unitId)
        {
            if (unitId <= 0) return BadRequest(new { message = "رقم الوحدة غير صحيح" });

            var resident = await _context.Residents
                .FirstOrDefaultAsync(r => r.unit_id == unitId);

            if (resident == null)
                return Ok(new List<object>());

            // ✅ الترتيب الصحيح بـ member_id
            var members = await _context.FamilyMembers
                .Where(m => m.resident_id == resident.resident_id)
                .OrderBy(m => m.member_id)
                .Select(m => new
                {
                    member_id = m.member_id,
                    first_name = m.first_name,
                    second_name = m.second_name,
                    third_name = m.third_name,
                    full_name = m.first_name + " " + m.second_name + " " + m.third_name,
                    national_id_front_path = m.national_id_front_path,
                    national_id_back_path = m.national_id_back_path
                })
                .ToListAsync();

            return Ok(members);
        }

        // ══════════════════════════════════════════════════════
        // مساعدات داخلية
        // ══════════════════════════════════════════════════════

        /// <summary>التحقق من نوع وحجم الصورة</summary>
        private static string? ValidateImage(IFormFile file)
        {
            if (file.Length == 0)
                return "الملف فارغ";

            if (file.Length > MaxFileSizeBytes)
                return "حجم الملف يتجاوز الحد المسموح (5 ميغابايت)";

            string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                return "نوع الملف غير مدعوم، المسموح: jpg, jpeg, png, webp";

            if (!AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
                return "نوع المحتوى غير مدعوم";

            return null; // صالح
        }

        /// <summary>حفظ ملف بأمان</summary>
        private static async Task SaveFileAsync(IFormFile file, string fullPath)
        {
            await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
            await file.CopyToAsync(stream);
        }

        /// <summary>حذف الملف القديم إن وُجد</summary>
        private void DeleteOldFile(string relativeFolder, string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            string fullPath = Path.Combine(_environment.WebRootPath, relativeFolder, fileName);
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }
    }

    // ══════════════════════════════════════════════════════
    // DTOs
    // ══════════════════════════════════════════════════════

    public class UpdateResidentDto
    {
        public int UnitId { get; set; }
        public string? PhoneNumber { get; set; }
        public string? ResidentType { get; set; }
        public int FamilyCount { get; set; }
        public IFormFile? ContractFront { get; set; }
        public IFormFile? ContractBack { get; set; }
    }

    public class FamilyMemberUploadDto
    {
        public int UnitId { get; set; }
        public int MemberIndex { get; set; }
        public string? MemberName { get; set; }
        public IFormFile? FrontPhoto { get; set; }
        public IFormFile? BackPhoto { get; set; }
    }
}