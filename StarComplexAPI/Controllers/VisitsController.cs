using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;
using StarComplexAPI.Models;

namespace StarComplexAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VisitsController : ControllerBase
    {
        private readonly StarComplexContext _context;

        public VisitsController(StarComplexContext context)
        {
            _context = context;
        }

        // ── إنشاء تصريح جديد ────────────────────────────────────────
        [HttpPost("CreateVisit")]
        public async Task<IActionResult> CreateVisit([FromBody] Visit? visit)
        {
            if (visit == null)
                return BadRequest(new { message = "بيانات الزيارة فارغة" });

            var unit = await _context.HousingUnits
                .FirstOrDefaultAsync(u => u.unit_id == visit.unit_id);

            if (unit == null)
                return BadRequest(new { message = "رقم الوحدة (unit_id) غير موجود في النظام" });

            if (string.IsNullOrWhiteSpace(visit.visitor_name))
                return BadRequest(new { message = "يجب إدخال اسم الزائر" });

            visit.visit_date ??= DateTime.Now;
            visit.visit_status = string.IsNullOrEmpty(visit.visit_status) ? "مقبولة" : visit.visit_status;

            // ── حساب تاريخ الانتهاء ─────────────────────────────────
            if (visit.expiry_date == null)
            {
                if (visit.visitor_type == "خط نقل طلاب")
                {
                    // ينتهي بنهاية الشهر القادم (شهر كامل من الآن)
                    var nextMonth = DateTime.Now.AddMonths(1);
                    visit.expiry_date = new DateTime(
                        nextMonth.Year,
                        nextMonth.Month,
                        DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month),
                        23, 59, 59
                    );
                }
                else
                {
                    // زيارة عادية: 24 ساعة
                    visit.expiry_date = visit.visit_date.Value.AddHours(24);
                }
            }

            try
            {
                visit.unit_id = unit.unit_id;
                _context.Visits.Add(visit);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "تم إنشاء التصريح بنجاح",
                    visitId = visit.visit_id,
                    unitId = unit.unit_id,
                    visitorName = visit.visitor_name,
                    status = visit.visit_status,
                    expires = visit.expiry_date?.ToString("yyyy-MM-dd HH:mm")
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "حدث خطأ أثناء الحفظ في قاعدة البيانات",
                    error = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        // ── جلب سجل الزيارات حسب الوحدة السكنية ────────────────────
        // GET api/Visits/GetByUnit/{unitId}
        [HttpGet("GetByUnit/{unitId}")]
        public async Task<IActionResult> GetByUnit(int unitId)
        {
            var unit = await _context.HousingUnits.FindAsync(unitId);
            if (unit == null)
                return NotFound(new { message = "الوحدة غير موجودة" });

            var visits = await _context.Visits
                .Where(v => v.unit_id == unitId)
                .OrderByDescending(v => v.visit_date)   // الأحدث أولاً
                .ToListAsync();

            return Ok(visits);
        }
    }
}