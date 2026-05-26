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
    public class ResidentsManagerController : ControllerBase
    {
        private readonly StarComplexContext _context;
        private readonly IWebHostEnvironment _environment;

        public ResidentsManagerController(StarComplexContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // =====================================================================
        // 1. جلب السكان النشطين مع بيانات الوحدة (يخدم CollectionView والبحث)
        // =====================================================================
        [HttpGet("active")]
        public async Task<ActionResult<IEnumerable<object>>> GetActiveResidents([FromQuery] string? search = "")
        {
            var query = _context.Residents
                .Include(r => r.HousingUnit)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(r =>
                    (r.first_name + " " + r.second_name + " " + r.third_name).Contains(search) ||
                    r.phone_number.Contains(search) ||
                    r.unit_id.ToString().Contains(search)
                );
            }

            var residents = await query.ToListAsync();

            var result = residents.Select(r => new
            {
                r.resident_id,
                r.unit_id,
                r.resident_code,
                r.first_name,
                r.second_name,
                r.third_name,
                FullName = r.FullName,
                r.phone_number,
                r.resident_type,
                r.contract_path_1,
                r.contract_path_2,
                r.family_members_count,
                unit_type = r.HousingUnit?.unit_type,
                unit_status = r.HousingUnit?.unit_status
            });

            return Ok(result);
        }

        // =====================================================================
        // 2. جلب ملف الساكن الكامل (العقد + هويته + هويات عائلته)
        // =====================================================================
        [HttpGet("profile/{id}")]
        public async Task<ActionResult<object>> GetResidentProfile(int id)
        {
            var resident = await _context.Residents
                .Include(r => r.HousingUnit)
                .Include(r => r.FamilyMembers)
                .FirstOrDefaultAsync(r => r.resident_id == id);

            if (resident == null)
                return NotFound($"الساكن رقم {id} غير موجود.");

            string baseUrl = $"{Request.Scheme}://{Request.Host}/uploads/";

            var profile = new
            {
                resident.resident_id,
                resident.unit_id,
                resident.resident_code,
                FullName = resident.FullName,
                resident.phone_number,
                resident.resident_type,
                resident.family_members_count,
                unit_type = resident.HousingUnit?.unit_type,
                contract_image_1 = resident.contract_path_1 != null
                    ? $"{baseUrl}contracts/{resident.contract_path_1}" : null,
                contract_image_2 = resident.contract_path_2 != null
                    ? $"{baseUrl}contracts/{resident.contract_path_2}" : null,
                family_members = resident.FamilyMembers.Select(m => new
                {
                    m.member_id,
                    FullName = m.FullName,
                    national_id_front = m.national_id_front_path != null
                        ? $"{baseUrl}ids/{m.national_id_front_path}" : null,
                    national_id_back = m.national_id_back_path != null
                        ? $"{baseUrl}ids/{m.national_id_back_path}" : null
                }).ToList()
            };

            return Ok(profile);
        }

        // =====================================================================
        // 3. جلب قائمة الأرشيف (مع FullName وresident_id مربوط بـ archive_id)
        // =====================================================================
        [HttpGet("archived")]
        public async Task<ActionResult<IEnumerable<object>>> GetArchivedResidents([FromQuery] string? search = "")
        {
            var query = _context.ResidentArchives.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(r =>
                    (r.first_name + " " + r.second_name + " " + r.third_name).Contains(search) ||
                    r.phone_number.Contains(search) ||
                    r.unit_id.ToString().Contains(search)
                );
            }

            var list = await query.OrderByDescending(r => r.move_out_date).ToListAsync();

            var result = list.Select(r => new
            {
                resident_id = r.archive_id,   // يُستخدم من زر الاستعادة والعرض
                r.unit_id,
                r.first_name,
                r.second_name,
                r.third_name,
                FullName = $"{r.first_name} {r.second_name} {r.third_name}".Trim(),
                r.phone_number,
                r.resident_type,
                r.contract_path_1,
                r.contract_path_2,
                resident_code = r.move_out_date.ToString("yyyy-MM-dd") // تاريخ الإخلاء كمرجع
            });

            return Ok(result);
        }

        // =====================================================================
        // 4. إضافة ساكن جديد مع صور العقد
        // =====================================================================
        [HttpPost("add")]
        public async Task<ActionResult<Resident>> AddResident(
            [FromForm] Resident resident,
            IFormFile? contractFile1,
            IFormFile? contractFile2)
        {
            ModelState.Remove("contract_path_1");
            ModelState.Remove("contract_path_2");

            if (!ModelState.IsValid) return BadRequest(ModelState);

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var unit = await _context.HousingUnits.FindAsync(resident.unit_id);
                if (unit == null)
                    return NotFound($"الوحدة رقم {resident.unit_id} غير موجودة.");
                if (unit.unit_status == "مشغول")
                    return Conflict("هذه الوحدة مشغولة بالفعل بساكن آخر.");

                _context.Residents.Add(resident);
                await _context.SaveChangesAsync();

                string contractsFolder = Path.Combine(_environment.WebRootPath, "uploads", "contracts");
                Directory.CreateDirectory(contractsFolder);

                if (contractFile1 != null)
                {
                    string fileName = $"res_{resident.resident_id}_c1{Path.GetExtension(contractFile1.FileName)}";
                    using var stream = new FileStream(Path.Combine(contractsFolder, fileName), FileMode.Create);
                    await contractFile1.CopyToAsync(stream);
                    resident.contract_path_1 = fileName;
                }

                if (contractFile2 != null)
                {
                    string fileName = $"res_{resident.resident_id}_c2{Path.GetExtension(contractFile2.FileName)}";
                    using var stream = new FileStream(Path.Combine(contractsFolder, fileName), FileMode.Create);
                    await contractFile2.CopyToAsync(stream);
                    resident.contract_path_2 = fileName;
                }

                unit.unit_status = "مشغول";

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(resident);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"خطأ داخلي: {ex.Message}");
            }
        }

        // =====================================================================
        // 5. إضافة فرد عائلة مع صور الهوية
        // =====================================================================
        [HttpPost("family-member/add")]
        public async Task<IActionResult> AddFamilyMember(
            [FromForm] FamilyMember member,
            IFormFile? idFront,
            IFormFile? idBack)
        {
            ModelState.Remove("national_id_front_path");
            ModelState.Remove("national_id_back_path");

            try
            {
                _context.FamilyMembers.Add(member);
                await _context.SaveChangesAsync();

                string idsFolder = Path.Combine(_environment.WebRootPath, "uploads", "ids");
                Directory.CreateDirectory(idsFolder);

                string prefix = $"mem{member.member_id}_res{member.resident_id}";

                if (idFront != null)
                {
                    string fileName = $"{prefix}_front{Path.GetExtension(idFront.FileName)}";
                    using var stream = new FileStream(Path.Combine(idsFolder, fileName), FileMode.Create);
                    await idFront.CopyToAsync(stream);
                    member.national_id_front_path = fileName;
                }

                if (idBack != null)
                {
                    string fileName = $"{prefix}_back{Path.GetExtension(idBack.FileName)}";
                    using var stream = new FileStream(Path.Combine(idsFolder, fileName), FileMode.Create);
                    await idBack.CopyToAsync(stream);
                    member.national_id_back_path = fileName;
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = "تم حفظ فرد العائلة بنجاح", member_id = member.member_id });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // =====================================================================
        // 6. إحصائيات لوحة التحكم
        // =====================================================================
        [HttpGet("dashboard-stats")]
        public async Task<IActionResult> GetDashboardStats()
        {
            var stats = new
            {
                totalResidents = await _context.Residents.CountAsync(),
                rentedCount = await _context.Residents.CountAsync(r => r.resident_type == "مؤجر"),
                archivedCount = await _context.ResidentArchives.CountAsync()
            };
            return Ok(stats);
        }

        // =====================================================================
        // 7. نقل الساكن للأرشيف (إخلاء وحدة)
        // =====================================================================
        [HttpPost("move-to-archive/{id}")]
        public async Task<IActionResult> MoveToArchive(int id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var resident = await _context.Residents
                    .Include(r => r.FamilyMembers)
                    .FirstOrDefaultAsync(r => r.resident_id == id);

                if (resident == null) return NotFound();

                var mainMember = resident.FamilyMembers.FirstOrDefault();

                var archive = new ResidentArchive
                {
                    unit_id = resident.unit_id,
                    first_name = resident.first_name,
                    second_name = resident.second_name,
                    third_name = resident.third_name,
                    resident_type = resident.resident_type,
                    phone_number = resident.phone_number,
                    move_out_date = DateTime.Now,
                    contract_path_1 = resident.contract_path_1,
                    contract_path_2 = resident.contract_path_2,
                    national_id_front_path = mainMember?.national_id_front_path,
                    national_id_back_path = mainMember?.national_id_back_path
                };

                _context.ResidentArchives.Add(archive);

                var unit = await _context.HousingUnits.FindAsync(resident.unit_id);
                if (unit != null) unit.unit_status = "فارغ";

                _context.FamilyMembers.RemoveRange(resident.FamilyMembers);
                _context.Residents.Remove(resident);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { message = "تمت أرشفة الساكن وإخلاء الوحدة" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, ex.Message);
            }
        }

        // =====================================================================
        // 8. استعادة ساكن من الأرشيف
        // =====================================================================
        [HttpPost("restore/{archiveId}")]
        public async Task<IActionResult> RestoreFromArchive(int archiveId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var archived = await _context.ResidentArchives.FindAsync(archiveId);
                if (archived == null) return NotFound();

                var unit = await _context.HousingUnits.FindAsync(archived.unit_id);
                if (unit == null) return NotFound($"الوحدة رقم {archived.unit_id} غير موجودة.");
                if (unit.unit_status == "مشغول")
                    return Conflict("هذه الوحدة مشغولة حالياً بساكن آخر.");

                var restoredResident = new Resident
                {
                    unit_id = archived.unit_id,
                    first_name = archived.first_name,
                    second_name = archived.second_name,
                    third_name = archived.third_name,
                    resident_type = archived.resident_type,
                    phone_number = archived.phone_number,
                    contract_path_1 = archived.contract_path_1,
                    contract_path_2 = archived.contract_path_2,
                    resident_code = Guid.NewGuid().ToString().Substring(0, 8).ToUpper(),
                    family_members_count = 1
                };

                _context.Residents.Add(restoredResident);
                unit.unit_status = "مشغول";

                _context.ResidentArchives.Remove(archived);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { message = "تمت استعادة الساكن بنجاح", resident_id = restoredResident.resident_id });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, ex.Message);
            }
        }

        // =====================================================================
        // 9. حذف صورة محددة (عقد أو هوية)
        // =====================================================================
        [HttpDelete("delete-image")]
        public async Task<IActionResult> DeleteImage(
            [FromQuery] int residentId,
            [FromQuery] int ownerId,
            [FromQuery] string imageType)
        {
            try
            {
                string contractsFolder = Path.Combine(_environment.WebRootPath, "uploads", "contracts");
                string idsFolder = Path.Combine(_environment.WebRootPath, "uploads", "ids");

                switch (imageType)
                {
                    case "contract_1":
                        {
                            var resident = await _context.Residents.FindAsync(residentId);
                            if (resident == null) return NotFound();
                            DeleteFile(contractsFolder, resident.contract_path_1);
                            resident.contract_path_1 = null;
                            break;
                        }
                    case "contract_2":
                        {
                            var resident = await _context.Residents.FindAsync(residentId);
                            if (resident == null) return NotFound();
                            DeleteFile(contractsFolder, resident.contract_path_2);
                            resident.contract_path_2 = null;
                            break;
                        }
                    case "id_front":
                        {
                            var member = await _context.FamilyMembers.FindAsync(ownerId);
                            if (member == null) return NotFound();
                            DeleteFile(idsFolder, member.national_id_front_path);
                            member.national_id_front_path = null;
                            break;
                        }
                    case "id_back":
                        {
                            var member = await _context.FamilyMembers.FindAsync(ownerId);
                            if (member == null) return NotFound();
                            DeleteFile(idsFolder, member.national_id_back_path);
                            member.national_id_back_path = null;
                            break;
                        }
                    case "member_front":
                        {
                            var member = await _context.FamilyMembers.FindAsync(ownerId);
                            if (member == null) return NotFound();
                            DeleteFile(idsFolder, member.national_id_front_path);
                            member.national_id_front_path = null;
                            break;
                        }
                    case "member_back":
                        {
                            var member = await _context.FamilyMembers.FindAsync(ownerId);
                            if (member == null) return NotFound();
                            DeleteFile(idsFolder, member.national_id_back_path);
                            member.national_id_back_path = null;
                            break;
                        }
                    default:
                        return BadRequest($"نوع الصورة غير معروف: {imageType}");
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = "تم حذف الصورة بنجاح" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"خطأ داخلي: {ex.Message}");
            }
        }

        // =====================================================================
        // دالة مساعدة لحذف الملف من القرص
        // =====================================================================
        private static void DeleteFile(string folder, string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            var path = Path.Combine(folder, fileName);
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }
}