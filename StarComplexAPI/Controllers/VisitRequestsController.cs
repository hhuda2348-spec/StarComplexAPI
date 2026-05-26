using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;
using StarComplexAPI.Models;

namespace StarComplexAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VisitRequestsController : ControllerBase
    {
        private readonly StarComplexContext _context;

        public VisitRequestsController(StarComplexContext context)
        {
            _context = context;
        }

        // 1. جلب البيانات مع دعم (البحث + التصفية بالحالة + التصفية بالتاريخ)
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Visit>>> GetRequests(
            [FromQuery] string? search,
            [FromQuery] string? status,
            [FromQuery] DateTime? date)
        {
            // نبدأ بإنشاء الاستعلام الأساسي
            var query = _context.Visits.AsQueryable();

            // الفلترة حسب البحث (الاسم، رقم السيارة، أو رقم الوحدة)
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(v =>
                    (v.visitor_name != null && v.visitor_name.Contains(search)) ||
                    (v.car_number != null && v.car_number.Contains(search)) ||
                    (v.unit_id.ToString().Contains(search))
                );
            }

            // الفلترة حسب الحالة (Status) - تأكدنا من استبعاد كلمة "الكل" إذا كانت مرسلة من الواجهة
            if (!string.IsNullOrEmpty(status) && status != "الكل")
            {
                query = query.Where(v => v.visit_status == status);
            }

            // الفلترة حسب التاريخ (Date)
            if (date.HasValue)
            {
                // مقارنة التاريخ فقط بدون الوقت لضمان دقة البحث
                query = query.Where(v => v.visit_date.HasValue && v.visit_date.Value.Date == date.Value.Date);
            }

            // ترتيب النتائج من الأحدث إلى الأقدم
            var results = await query.OrderByDescending(v => v.visit_id).ToListAsync();

            // معالجة البيانات قبل إرسالها لضمان عدم وجود قيم Null تسبب مشاكل في واجهة MAUI
            foreach (var v in results)
            {
                v.visitor_name ??= "غير معروف";
                v.car_number ??= "لا يوجد";
                v.visit_status ??= "مقبولة";
                v.visitor_type ??= "زيارة عادية";
                v.selected_days ??= "";
                v.selected_month ??= "";
                v.morning_window ??= "";
                v.afternoon_window ??= "";
            }

            return Ok(results);
        }

        // 2. إنشاء طلب جديد (متوافق مع منطق خط نقل الطلاب والزيارات العادية)
        [HttpPost("create")]
        public async Task<IActionResult> CreateRequest([FromBody] Visit visit)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // التحقق من وجود الوحدة السكنية
            var unitExists = await _context.HousingUnits.AnyAsync(u => u.unit_id == visit.unit_id);
            if (!unitExists)
            {
                return BadRequest("رقم الوحدة المدخل غير مسجل في النظام.");
            }

            // منطق تحديد الصلاحية بناءً على نوع الزائر
            if (visit.visitor_type == "خط نقل طلاب")
            {
                // صلاحية لمدة شهر (30 يوم)
                visit.expiry_date = DateTime.Now.AddDays(30);
                visit.visit_status = "مقبولة";
            }
            else
            {
                // الزيارات العادية تنتهي بعد 24 ساعة
                visit.expiry_date = DateTime.Now.AddHours(24);
                visit.visit_status = "مقبولة";
            }

            // إذا لم يتم إرسال تاريخ زيارة، نعتمد تاريخ اليوم
            if (!visit.visit_date.HasValue)
            {
                visit.visit_date = DateTime.Now;
            }

            _context.Visits.Add(visit);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "تم إنشاء الطلب وتوليد البيانات بنجاح",
                data = visit
            });
        }

        // 3. تحديث الحالة (قبول / رفض)
        [HttpPatch("{id}/update-status")]
        public async Task<IActionResult> UpdateRequestStatus(int id, [FromBody] StatusUpdateRequest request)
        {
            var visit = await _context.Visits.FindAsync(id);
            if (visit == null) return NotFound("الطلب غير موجود.");

            visit.visit_status = request.NewStatus;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = $"تم تحديث الحالة إلى {request.NewStatus}" });
        }

        // 4. حذف سجل
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRequest(int id)
        {
            var visit = await _context.Visits.FindAsync(id);
            if (visit == null) return NotFound();

            _context.Visits.Remove(visit);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "تم حذف السجل بنجاح" });
        }
    }

    // كلاس مساعد لتلقي تحديث الحالة
    public class StatusUpdateRequest
    {
        public string NewStatus { get; set; }
    }
}