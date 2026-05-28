using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using StarComplexAPI.Data;
using StarComplexAPI.Models;

namespace StarComplexAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SecurityController : ControllerBase
    {
        private readonly StarComplexContext _context;

        public SecurityController(StarComplexContext context)
        {
            _context = context;
        }

        // ══════════════════════════════════════════════════════════
        //  HELPER — تطبيع النص العربي
        // ══════════════════════════════════════════════════════════
        private static string NormaliseArabic(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = string.Join(" ", s.Trim().Split(' ',
                StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()));
            s = s.Replace("أ", "ا").Replace("إ", "ا").Replace("آ", "ا")
                 .Replace("ؤ", "و").Replace("ئ", "ي")
                 .Replace("ة", "ه")
                 .Replace("\u06CC", "ي").Replace("\u0649", "ي")
                 .Replace("\u06A9", "ك");
            s = new string(s.Where(c => c < '\u064B' || c > '\u065F').ToArray());
            return s.Trim();
        }

        // ══════════════════════════════════════════════════════════
        //  HELPER — التحقق من نوع الموظف
        // ══════════════════════════════════════════════════════════
        private static bool IsSecurityEmployee(Employee e)
        {
            var empType = NormaliseArabic(e.employee_type?.Trim() ?? "");
            var jobTitle = NormaliseArabic(e.job_title?.Trim() ?? "");

            bool byType = empType == "امن" || empType.Contains("امن") || empType == "security";
            bool byTitle = jobTitle == "امن" || jobTitle.Contains("امن") || jobTitle.Contains("حراسه")
                                             || jobTitle.Contains("حراسة") || jobTitle == "موظف امن";
            return byType || byTitle;
        }

        // ══════════════════════════════════════════════════════════
        //  HELPER — التحقق من موظف الأمن
        // ══════════════════════════════════════════════════════════
        private async Task<(Employee? employee, string? error)> AuthEmployee(
            string? code, string? fullName)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(fullName))
                return (null, "بيانات الموظف غير مكتملة");

            var cleanCode = code.Trim();
            var cleanName = NormaliseArabic(fullName);

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e =>
                    (e.employee_code ?? "").Trim() == cleanCode);

            if (employee == null)
                return (null, "بيانات الموظف غير صحيحة");

            var dbFullName = NormaliseArabic(string.Join(" ",
                new[] { employee.first_name, employee.second_name, employee.third_name }
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!.Trim())));

            if (!string.Equals(dbFullName, cleanName, StringComparison.Ordinal))
                return (null, "بيانات الموظف غير صحيحة");

            if (!IsSecurityEmployee(employee))
                return (null, "ليس لديك صلاحية الوصول لبوابة الأمن");

            return (employee, null);
        }

        // ══════════════════════════════════════════════════════════
        //  HELPER — كتابة سجل أمني
        // ══════════════════════════════════════════════════════════
        private async Task WriteLog(
            int employeeId, string actionType, string actionResult,
            string? notes, int? visitId = null,
            string? visitorSnapshot = null, int? unitSnapshot = null)
        {
            _context.SecurityLogs.Add(new SecurityLog
            {
                employee_id = employeeId,
                visit_id = visitId,
                action_type = actionType,
                action_result = actionResult,
                notes = notes,
                visitor_snapshot = visitorSnapshot,
                unit_id_snapshot = unitSnapshot,
                created_at = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }

        // ══════════════════════════════════════════════════════════
        //  HELPER — فحص حالة التصريح قبل أي عملية مسح
        //  يُرجع: (isValid, statusCode, reasonAr)
        //  ✅ يُميّز بين: منتهية / محظور / داخل_الآن / مقبولة
        // ══════════════════════════════════════════════════════════
        private static (bool canEnter, string statusCode, string reasonAr)
            EvaluateVisitState(Visit visit, Blacklist? blacklistEntry)
        {
            // 1. قائمة سوداء
            if (blacklistEntry != null)
                return (false, "BLOCKED", "الشخص مدرج في القائمة السوداء");

            // 2. منتهية الصلاحية (تاريخ)
            if (visit.expiry_date.HasValue && visit.expiry_date.Value < DateTime.Now)
                return (false, "EXPIRED", "انتهت صلاحية هذا التصريح");

            // 3. الزائر داخل الآن — لا يُسمح بالدخول مجدداً
            if (visit.visit_status == "داخل الآن")
                return (false, "ALREADY_IN", "الزائر مسجّل داخل المجمع بالفعل — يُرجى تسجيل الخروج أولاً");

            // 4. مرفوضة
            if (visit.visit_status == "مرفوضة")
                return (false, "REJECTED", "هذا التصريح مرفوض");

            // 5. حالة أخرى مُنهية
            if (visit.visit_status == "منتهية")
                return (false, "EXPIRED", "انتهت صلاحية هذا التصريح");

            // 6. مقبولة — يُسمح
            return (true, "APPROVED", "التصريح صالح ومصرح به");
        }

        // ══════════════════════════════════════════════════════════
        //  1. التحقق من الموظف
        //     GET api/Security/VerifyEmployee
        // ══════════════════════════════════════════════════════════
        [HttpGet("VerifyEmployee")]
        public async Task<IActionResult> VerifyEmployee(
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName)
        {
            var (employee, error) = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new { valid = false, message = error });

            string dbFullName = string.Join(" ",
                new[] { employee.first_name, employee.second_name, employee.third_name }
                .Where(n => !string.IsNullOrWhiteSpace(n)));

            return Ok(new
            {
                valid = true,
                employeeId = employee.employee_id,
                fullName = dbFullName,
                firstName = employee.first_name,
                jobTitle = employee.job_title,
                employeeType = employee.employee_type,
                employeeCode = employee.employee_code
            });
        }

        // ══════════════════════════════════════════════════════════
        //  2. فحص التصريح
        //     GET api/Security/ScanQR/{visitId}
        //  ✅ معدّل: يُرجع حالة واضحة لكل سيناريو
        // ══════════════════════════════════════════════════════════
        [HttpGet("ScanQR/{visitId}")]
        public async Task<IActionResult> ScanQR(
            int visitId,
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName)
        {
            var (employee, error) = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new
                {
                    authorized = false,
                    status = "رفض",
                    reason = error
                });

            var visit = await _context.Visits
                .FirstOrDefaultAsync(v => v.visit_id == visitId);
            if (visit == null)
                return NotFound(new
                {
                    authorized = false,
                    status = "غير موجود",
                    reason = "التصريح لم يُعثر عليه في النظام"
                });

            string empName = string.Join(" ",
                new[] { employee.first_name, employee.second_name }
                .Where(n => !string.IsNullOrWhiteSpace(n)));

            // ── فحص القائمة السوداء ──
            var blacklistEntry = await _context.Blacklist
                .Where(b => b.person_name != null && visit.visitor_name != null &&
                            visit.visitor_name.Contains(b.person_name))
                .FirstOrDefaultAsync();

            // ── تقييم الحالة الكاملة ──
            var (canEnter, statusCode, reasonAr) = EvaluateVisitState(visit, blacklistEntry);

            // ── تحديث حالة الزيارة في حال انتهاء الصلاحية ──
            if (statusCode == "EXPIRED" && visit.visit_status != "منتهية")
            {
                visit.visit_status = "منتهية";
                await _context.SaveChangesAsync();
            }

            // ── تسجيل السجل الأمني ──
            string logAction = blacklistEntry != null ? "BLACKLIST_HIT" : "SCAN";
            await WriteLog(employee.employee_id, logAction,
                canEnter ? "APPROVED" : "REJECTED",
                $"فحص تصريح — النتيجة: {statusCode}",
                visitId, visit.visitor_name, visit.unit_id);

            var result = new ScanResult
            {
                authorized = canEnter,
                status = statusCode switch
                {
                    "APPROVED" => visit.visit_status ?? "مقبولة",
                    "EXPIRED" => "منتهية",
                    "BLOCKED" => "محظور",
                    "ALREADY_IN" => "داخل الآن",
                    "REJECTED" => "مرفوضة",
                    _ => visit.visit_status ?? "غير معروف"
                },
                reason = reasonAr,
                visit = MapVisit(visit),
                employeeName = empName,
                blacklistReason = blacklistEntry?.reason,
                statusCode = statusCode
            };

            return Ok(result);
        }

        // ══════════════════════════════════════════════════════════
        //  3. تسجيل الدخول
        //     POST api/Security/RecordEntry/{visitId}
        //  ✅ معدّل: يرفض إعادة الدخول إذا الزائر داخل الآن
        // ══════════════════════════════════════════════════════════
        [HttpPost("RecordEntry/{visitId}")]
        public async Task<IActionResult> RecordEntry(
            int visitId,
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName)
        {
            var (employee, error) = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new { message = error });

            var visit = await _context.Visits.FindAsync(visitId);
            if (visit == null)
                return NotFound(new { message = "التصريح غير موجود" });

            // ✅ منع تسجيل الدخول مرتين
            if (visit.visit_status == "داخل الآن")
                return BadRequest(new
                {
                    message = "الزائر مسجّل داخل المجمع بالفعل — يُرجى تسجيل الخروج أولاً",
                    statusCode = "ALREADY_IN",
                    currentStatus = visit.visit_status
                });

            // ✅ منع تسجيل الدخول للتصاريح المنتهية أو المرفوضة
            if (visit.visit_status == "منتهية" || visit.visit_status == "مرفوضة")
                return BadRequest(new
                {
                    message = $"لا يمكن تسجيل دخول لتصريح بحالة: {visit.visit_status}",
                    statusCode = "EXPIRED",
                    currentStatus = visit.visit_status
                });

            if (visit.expiry_date.HasValue && visit.expiry_date.Value < DateTime.Now)
                return BadRequest(new
                {
                    message = "انتهت صلاحية التصريح",
                    statusCode = "EXPIRED",
                    currentStatus = "منتهية"
                });

            visit.visit_status = "داخل الآن";
            await _context.SaveChangesAsync();
            await WriteLog(employee.employee_id, "ENTRY", "APPROVED",
                "تم تسجيل دخول الزائر ✅", visitId, visit.visitor_name, visit.unit_id);

            return Ok(new
            {
                message = "تم تسجيل الدخول",
                entryTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                visitId,
                newStatus = "داخل الآن"
            });
        }

        // ══════════════════════════════════════════════════════════
        //  4. تسجيل الخروج
        //     POST api/Security/RecordExit/{visitId}
        // ══════════════════════════════════════════════════════════
        [HttpPost("RecordExit/{visitId}")]
        public async Task<IActionResult> RecordExit(
            int visitId,
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName)
        {
            var (employee, error) = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new { message = error });

            var visit = await _context.Visits.FindAsync(visitId);
            if (visit == null)
                return NotFound(new { message = "التصريح غير موجود" });

            // ✅ يُسمح بالخروج فقط إذا كان الزائر داخل الآن
            if (visit.visit_status != "داخل الآن")
                return BadRequest(new
                {
                    message = $"لا يمكن تسجيل خروج لزائر بحالة: {visit.visit_status}",
                    statusCode = visit.visit_status
                });

            visit.visit_status = "منتهية";
            visit.expiry_date = DateTime.Now;
            await _context.SaveChangesAsync();
            await WriteLog(employee.employee_id, "EXIT", "INFO",
                "تم تسجيل خروج الزائر وإنهاء التصريح 🚪",
                visitId, visit.visitor_name, visit.unit_id);

            return Ok(new
            {
                message = "تم تسجيل الخروج وإنهاء التصريح",
                exitTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                visitId,
                newStatus = "منتهية"
            });
        }

        // ══════════════════════════════════════════════════════════
        //  5. الزيارات النشطة
        //     GET api/Security/ActiveVisits
        // ══════════════════════════════════════════════════════════
        [HttpGet("ActiveVisits")]
        public async Task<IActionResult> GetActiveVisits()
        {
            var visits = await _context.Visits
                .Where(v => v.visit_status == "مقبولة" || v.visit_status == "داخل الآن")
                .Where(v => v.expiry_date == null || v.expiry_date > DateTime.Now)
                .OrderByDescending(v => v.visit_date)
                .Select(v => new VisitDto
                {
                    visit_id = v.visit_id,
                    visitor_name = v.visitor_name,
                    visitor_type = v.visitor_type,
                    car_number = v.car_number,
                    unit_id = v.unit_id,
                    visit_status = v.visit_status,
                    visit_date = v.visit_date,
                    expiry_date = v.expiry_date,
                    selected_days = v.selected_days,
                    morning_window = v.morning_window,
                    afternoon_window = v.afternoon_window
                })
                .ToListAsync();

            return Ok(visits);
        }

        // ══════════════════════════════════════════════════════════
        //  6. إرسال بلاغ
        //     POST api/Security/SubmitReport
        // ══════════════════════════════════════════════════════════
        [HttpPost("SubmitReport")]
        public async Task<IActionResult> SubmitReport([FromBody] SubmitReportRequest req)
        {
            var (employee, error) = await AuthEmployee(req.EmployeeCode, req.EmployeeFullName);
            if (employee == null)
                return Unauthorized(new { message = error });

            if (string.IsNullOrWhiteSpace(req.Notes))
                return BadRequest(new { message = "يرجى كتابة تفاصيل البلاغ" });

            Visit? visit = null;
            if (req.VisitId.HasValue)
                visit = await _context.Visits.FindAsync(req.VisitId.Value);

            await WriteLog(employee.employee_id, "REPORT", req.Severity ?? "WARNING",
                req.Notes, req.VisitId, visit?.visitor_name, visit?.unit_id);

            string empName = string.Join(" ",
                new[] { employee.first_name, employee.second_name }
                .Where(n => !string.IsNullOrWhiteSpace(n)));

            return Ok(new
            {
                message = "تم حفظ البلاغ بنجاح",
                log_time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                employee = empName
            });
        }

        // ══════════════════════════════════════════════════════════
        //  7. سجل إجراءات الموظف
        //     GET api/Security/MyLogs
        // ══════════════════════════════════════════════════════════
        [HttpGet("MyLogs")]
        public async Task<IActionResult> GetMyLogs(
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName,
            [FromQuery] int pageSize = 50)
        {
            var (employee, error) = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new { message = error });

            var logs = await _context.SecurityLogs
                .Where(l => l.employee_id == employee.employee_id)
                .OrderByDescending(l => l.created_at)
                .Take(pageSize)
                .Select(l => new
                {
                    l.log_id,
                    l.action_type,
                    l.action_result,
                    l.notes,
                    l.visit_id,
                    l.visitor_snapshot,
                    l.unit_id_snapshot,
                    l.created_at
                })
                .ToListAsync();

            return Ok(logs);
        }

        // ══════════════════════════════════════════════════════════
        //  8. تفعيل الطوارئ
        //     POST api/Security/Emergency
        // ══════════════════════════════════════════════════════════
        [HttpPost("Emergency")]
        public async Task<IActionResult> TriggerEmergency(
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName,
            [FromQuery] string? notes)
        {
            var (employee, error) = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new { message = error });

            await WriteLog(employee.employee_id, "EMERGENCY", "WARNING",
                notes ?? "تفعيل حالة طوارئ — فتح جميع البوابات 🚨");

            string empName = string.Join(" ",
                new[] { employee.first_name, employee.second_name }
                .Where(n => !string.IsNullOrWhiteSpace(n)));

            return Ok(new
            {
                message = "تم تفعيل حالة الطوارئ وإبلاغ الإدارة",
                time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                employee = empName
            });
        }

        // ══════════════════════════════════════════════════════════
        //  9. صفحة QR الويب
        //     GET api/Security/QRPage/{visitId}
        //  ✅ معدّل: عرض حالة "داخل الآن" بأزرار مناسبة
        // ══════════════════════════════════════════════════════════
        [HttpGet("QRPage/{visitId}")]
        public async Task<IActionResult> QRPage(
            int visitId,
            [FromQuery] string? token,
            [FromQuery] string? name)
        {
            var (employee, _) = await AuthEmployee(token, name);
            if (employee == null)
                return Content(BuildBlankPage(), "text/html");

            var visit = await _context.Visits
                .FirstOrDefaultAsync(v => v.visit_id == visitId);
            if (visit == null)
                return Content(BuildErrorPage("التصريح غير موجود"), "text/html");

            var blacklistEntry = await _context.Blacklist
                .Where(b => b.person_name != null && visit.visitor_name != null &&
                            visit.visitor_name.Contains(b.person_name))
                .FirstOrDefaultAsync();

            var (canEnter, statusCode, _) = EvaluateVisitState(visit, blacklistEntry);

            string empName = string.Join(" ",
                new[] { employee.first_name, employee.second_name }
                .Where(n => !string.IsNullOrWhiteSpace(n)));
            string blockReason = blacklistEntry?.reason ?? string.Empty;

            await WriteLog(employee.employee_id, "SCAN",
                canEnter ? "APPROVED" : "REJECTED",
                $"فتح صفحة التصريح #{visitId} — النتيجة: {statusCode}",
                visitId, visit.visitor_name, visit.unit_id);

            return Content(
                BuildQRPage(visit, statusCode, empName, blockReason, token!, name!),
                "text/html");
        }

        // ══════════════════════════════════════════════════════════
        //  10. حالة زيارة محددة — للـ polling
        //      GET api/Security/VisitStatus/{visitId}
        // ══════════════════════════════════════════════════════════
        [HttpGet("VisitStatus/{visitId}")]
        public async Task<IActionResult> GetVisitStatus(int visitId)
        {
            var visit = await _context.Visits
                .Where(v => v.visit_id == visitId)
                .Select(v => new
                {
                    v.visit_id,
                    v.visit_status,
                    v.visitor_name,
                    v.visitor_type,
                    v.car_number,
                    v.unit_id,
                    v.expiry_date
                })
                .FirstOrDefaultAsync();

            if (visit == null)
                return NotFound(new { message = "الزيارة غير موجودة" });

            return Ok(visit);
        }

        // ══════════════════════════════════════════════════════════
        //  11. رفض زيارة
        //      POST api/Security/RejectVisit/{visitId}
        // ══════════════════════════════════════════════════════════
        [HttpPost("RejectVisit/{visitId}")]
        public async Task<IActionResult> RejectVisit(
            int visitId,
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName)
        {
            var (employee, error) = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new { message = error });

            var visit = await _context.Visits.FindAsync(visitId);
            if (visit == null)
                return NotFound(new { message = "التصريح غير موجود" });

            if (visit.visit_status == "داخل الآن" || visit.visit_status == "منتهية")
                return BadRequest(new
                {
                    message = $"لا يمكن رفض زيارة بحالة: {visit.visit_status}"
                });

            visit.visit_status = "مرفوضة";
            await _context.SaveChangesAsync();
            await WriteLog(employee.employee_id, "REPORT", "REJECTED",
                $"تم رفض التصريح #{visitId} 🚫",
                visitId, visit.visitor_name, visit.unit_id);

            string empName = string.Join(" ",
                new[] { employee.first_name, employee.second_name }
                .Where(n => !string.IsNullOrWhiteSpace(n)));

            return Ok(new
            {
                message = "تم رفض الزيارة",
                rejectTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                employee = empName,
                visitId
            });
        }

        // ══ Helpers ═══════════════════════════════════════════════
        private static VisitDto MapVisit(Visit v) => new()
        {
            visit_id = v.visit_id,
            visitor_name = v.visitor_name,
            visitor_type = v.visitor_type,
            car_number = v.car_number,
            unit_id = v.unit_id,
            visit_status = v.visit_status,
            visit_date = v.visit_date,
            expiry_date = v.expiry_date,
            selected_days = v.selected_days,
            morning_window = v.morning_window,
            afternoon_window = v.afternoon_window
        };

        private static string BuildBlankPage() => """
            <!DOCTYPE html><html><head><meta charset="utf-8">
            <style>html,body{margin:0;background:#0a0a0f;height:100%}</style>
            </head><body></body></html>
            """;

        private static string BuildErrorPage(string msg) =>
            $"<html dir='rtl'><head><meta charset='utf-8'><style>" +
            "body{{font-family:sans-serif;display:flex;align-items:center;" +
            "justify-content:center;height:100vh;background:#0a0a0f;color:#fff;margin:0}}" +
            $"</style></head><body><h2>{msg}</h2></body></html>";

        // ══════════════════════════════════════════════════════════
        //  BuildQRPage — معدّل بالكامل
        //  ✅ يدعم: APPROVED / BLOCKED / EXPIRED / ALREADY_IN / REJECTED
        //  ✅ زر تسجيل الخروج يظهر مباشرة عند ALREADY_IN
        //  ✅ polling كل 4 ثوانٍ لمزامنة الحالة
        // ══════════════════════════════════════════════════════════
        private static string BuildQRPage(
            Visit v, string statusCode, string empName,
            string blockReason, string token, string name)
        {
            var (accentColor, statusIcon, statusAr) = statusCode switch
            {
                "APPROVED" => ("#22C55E", "✅", "مقبول — يُسمح بالدخول"),
                "BLOCKED" => ("#EF4444", "🚫", "محظور — مرفوض أمنياً"),
                "ALREADY_IN" => ("#3B82F6", "🔵", "الزائر داخل المجمع"),
                "REJECTED" => ("#EF4444", "🚫", "مرفوض"),
                _ => ("#F59E0B", "⏰", "منتهي الصلاحية"),
            };

            string extraRows = v.visitor_type == "خط نقل طلاب"
                ? $"""
           <div class="row"><span class="lbl">أيام الدوام</span><span class="val">{v.selected_days ?? "—"}</span></div>
           <div class="row"><span class="lbl">وقت الذهاب</span><span class="val">{v.morning_window ?? "—"}</span></div>
           <div class="row"><span class="lbl">وقت الإياب</span><span class="val">{v.afternoon_window ?? "—"}</span></div>
           """
                : $"""<div class="row"><span class="lbl">رقم السيارة</span><span class="val car">🚗 {v.car_number ?? "لا يوجد"}</span></div>""";

            string blacklistRow = statusCode == "BLOCKED" && !string.IsNullOrEmpty(blockReason)
                ? $"""<div class="row danger"><span class="lbl">سبب الحظر</span><span class="val" style="color:#EF4444">{blockReason}</span></div>"""
                : "";

            string expiryFormatted = v.expiry_date.HasValue
                ? v.expiry_date.Value.ToString("yyyy/MM/dd HH:mm") : "—";

            // ✅ الأزرار تعتمد على الحالة:
            // APPROVED   → زر قبول الدخول + رفض
            // ALREADY_IN → زر تسجيل الخروج فقط
            // غير ذلك   → لا أزرار
            string actionButtons = statusCode switch
            {
                "APPROVED" => """
        <div class="btn-row" id="actionBtns">
          <button class="btn btn-accept" onclick="recordAction('entry')">
            <span class="btn-icon">✅</span>قبول الدخول
          </button>
          <button class="btn btn-reject" onclick="recordAction('reject')">
            <span class="btn-icon">🚫</span>رفض الدخول
          </button>
        </div>
        """,
                "ALREADY_IN" => """
        <div class="btn-row single" id="actionBtns">
          <button class="btn btn-exit" onclick="recordAction('exit')">
            <span class="btn-icon">🚪</span>تسجيل خروج الزائر
          </button>
        </div>
        """,
                _ => ""
            };

            string visitIdStr = v.visit_id?.ToString() ?? "—";
            string visitorName = v.visitor_name ?? "—";
            string visitorType = v.visitor_type ?? "—";
            string unitId = v.unit_id?.ToString() ?? "—";
            string visitStatus = v.visit_status ?? "—";
            string nowFmt = DateTime.Now.ToString("HH:mm  yyyy/MM/dd");
            string nameJs = name.Replace("\\", "\\\\").Replace("'", "\\'");
            string tokenJs = token.Replace("\\", "\\\\").Replace("'", "\\'");

            // حالة polling — نتوقف إذا كانت الحالة نهائية
            bool shouldPoll = statusCode == "APPROVED" || statusCode == "ALREADY_IN";

            return $$"""
        <!DOCTYPE html>
        <html dir="rtl" lang="ar">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1">
          <title>تصريح #{{visitIdStr}}</title>
          <link href="https://fonts.googleapis.com/css2?family=Tajawal:wght@400;500;700;900&display=swap" rel="stylesheet">
          <style>
            *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
            :root {
              --accent: {{accentColor}};
              --bg: #0b0204;
              --surface: rgba(25, 6, 10, 0.65);
              --panel: rgba(45, 12, 18, 0.45);
              --card: rgba(65, 18, 26, 0.6);
              --border: rgba(239, 68, 68, 0.15);
              --text: #FFFFFF;
              --muted: #CDBCBF;
              --gold: #E5C158;
              --gold-dim: rgba(229, 193, 88, 0.1);
            }
            body {
              font-family: 'Tajawal', sans-serif;
              background: radial-gradient(circle at center, #1f050a 0%, var(--bg) 100%);
              color: var(--text);
              min-height: 100vh;
              display: flex;
              justify-content: center;
              align-items: center;
              padding: 20px;
              -webkit-font-smoothing: antialiased;
            }
            .main-container {
              width: 100%;
              max-width: 520px;
              background: var(--surface);
              border: 1px solid rgba(255,255,255,0.06);
              border-radius: 28px;
              padding: 30px;
              backdrop-filter: blur(16px);
              -webkit-backdrop-filter: blur(16px);
              box-shadow: 0 20px 50px rgba(0,0,0,0.6);
              animation: fadeIn 0.5s cubic-bezier(0.16,1,0.3,1);
            }
            @keyframes fadeIn { from{opacity:0;transform:translateY(20px)} to{opacity:1;transform:translateY(0)} }
            .header { text-align:center; margin-bottom:25px; padding-bottom:20px; border-bottom:1px solid rgba(255,255,255,0.08); }
            .status-icon { font-size:56px; display:block; margin-bottom:12px; filter:drop-shadow(0 0 20px var(--accent)); }
            .status-badge {
              display:inline-block; background:rgba(255,255,255,0.05); color:#fff;
              border:1px solid var(--accent); padding:8px 24px; border-radius:50px;
              font-size:16px; font-weight:700; box-shadow:0 4px 15px rgba(0,0,0,0.2);
            }
            .permit-id { margin-top:12px; font-size:13px; color:var(--gold); font-weight:700; letter-spacing:0.5px; }
            .section-title { font-size:14px; font-weight:700; color:var(--gold); margin:20px 0 12px; display:flex; align-items:center; gap:6px; }
            .rows { display:flex; flex-direction:column; gap:10px; margin-bottom:20px; }
            .row {
              display:flex; justify-content:space-between; align-items:center;
              padding:14px 18px; background:var(--panel); border:1px solid rgba(255,255,255,0.03);
              border-radius:14px; transition:all .25s ease;
            }
            .row:hover { background:var(--card); border-color:rgba(255,255,255,0.08); transform:translateX(-3px); }
            .row.danger { background:rgba(239,68,68,0.1); border-color:rgba(239,68,68,0.2); }
            .lbl { font-size:14px; color:var(--muted); font-weight:500; }
            .val { font-size:15px; font-weight:700; color:var(--text); }
            .val.accent { color:var(--gold); }
            .status-chip { display:inline-flex; align-items:center; gap:6px; padding:6px 14px; border-radius:12px; font-size:13px; font-weight:700; }
            .chip-accepted { background:rgba(34,197,94,0.15); color:#22C55E; border:1px solid rgba(34,197,94,0.3); }
            .chip-inside   { background:rgba(59,130,246,0.15); color:#3B82F6; border:1px solid rgba(59,130,246,0.3); }
            .chip-rejected { background:rgba(239,68,68,0.15); color:#EF4444; border:1px solid rgba(239,68,68,0.3); }
            .chip-expired  { background:rgba(245,158,11,0.15); color:#F59E0B; border:1px solid rgba(245,158,11,0.3); }
            .action-result {
              padding:15px; border-radius:14px; text-align:center; font-size:14px;
              font-weight:700; display:none; margin-bottom:15px;
            }
            .action-result.ok  { background:rgba(34,197,94,0.15); color:#22C55E; border:1px solid rgba(34,197,94,0.3); }
            .action-result.err { background:rgba(239,68,68,0.15); color:#EF4444; border:1px solid rgba(239,68,68,0.3); }
            .btn-row { display:grid; grid-template-columns:1fr 1fr; gap:12px; margin-bottom:20px; }
            .btn-row.single { grid-template-columns:1fr; }
            .btn {
              height:52px; border:none; border-radius:14px; font-family:'Tajawal',sans-serif;
              font-size:15px; font-weight:700; cursor:pointer; transition:all .2s ease;
              display:flex; align-items:center; justify-content:center; gap:6px;
            }
            .btn-accept { background:#22C55E; color:#05160b; box-shadow:0 4px 15px rgba(34,197,94,0.25); }
            .btn-accept:hover:not(:disabled) { background:#16a34a; transform:translateY(-2px); }
            .btn-reject { background:#EF4444; color:#fff; box-shadow:0 4px 15px rgba(239,68,68,0.25); }
            .btn-reject:hover:not(:disabled) { background:#dc2626; transform:translateY(-2px); }
            .btn-exit { background:#3B82F6; color:#fff; box-shadow:0 4px 15px rgba(59,130,246,0.3); }
            .btn-exit:hover:not(:disabled) { background:#2563eb; transform:translateY(-2px); }
            .btn:disabled { opacity:0.4; cursor:not-allowed; transform:none !important; }
            .poll-indicator {
              display:flex; align-items:center; gap:8px; justify-content:center;
              font-size:11px; color:var(--muted); margin-bottom:12px;
            }
            .poll-dot { width:8px; height:8px; background:#22C55E; border-radius:50%; animation:pulse 2s infinite; }
            @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:0.3} }
            .report-wrap {
              background:rgba(255,255,255,0.02); border:1px solid rgba(255,255,255,0.05);
              border-radius:18px; padding:16px; margin-top:15px;
            }
            #reportNotes {
              width:100%; height:80px; background:rgba(0,0,0,0.3);
              border:1px solid rgba(255,255,255,0.08); border-radius:10px;
              color:var(--text); font-family:'Tajawal',sans-serif; font-size:13px;
              padding:12px; resize:none; outline:none; transition:all .2s;
            }
            #reportNotes:focus { border-color:var(--gold); box-shadow:0 0 0 3px var(--gold-dim); }
            #reportNotes::placeholder { color:rgba(255,255,255,0.25); }
            #reportSeverity {
              width:100%; margin-top:10px; padding:10px; background:#1a080d;
              border:1px solid rgba(255,255,255,0.08); border-radius:10px;
              color:var(--text); font-family:'Tajawal',sans-serif; font-size:13px; outline:none;
            }
            .btn-report {
              width:100%; height:42px; margin-top:12px; border:none; border-radius:10px;
              background:linear-gradient(135deg,#E5C158,#AA8417); color:#1a1200;
              font-family:'Tajawal',sans-serif; font-size:14px; font-weight:700; cursor:pointer; transition:all .2s;
            }
            .btn-report:hover { transform:translateY(-1px); box-shadow:0 4px 15px rgba(229,193,88,0.3); }
            #reportMsg { font-size:12px; margin-top:10px; display:none; text-align:center; font-weight:700; padding:8px; border-radius:8px; }
            #reportMsg.ok  { background:rgba(34,197,94,0.1); color:#22C55E; }
            #reportMsg.err { background:rgba(239,68,68,0.1); color:#EF4444; }
            .security-note {
              margin-top:20px; padding:10px; background:rgba(229,193,88,0.05);
              border:1px solid rgba(229,193,88,0.15); border-radius:10px;
              font-size:11px; color:var(--gold); text-align:center; font-weight:500;
            }
            .footer {
              margin-top:25px; padding-top:14px; border-top:1px solid rgba(255,255,255,0.06);
              display:flex; justify-content:space-between; font-size:12px; color:var(--muted);
            }
          </style>
        </head>
        <body>
          <div class="main-container">
            <div class="header">
              <span class="status-icon" id="mainIcon">{{statusIcon}}</span>
              <div class="status-badge" id="mainBadge">{{statusAr}}</div>
              <div class="permit-id">🔐 رقم التصريح: #{{visitIdStr}}</div>
            </div>

            {{(shouldPoll ? """
            <div class="poll-indicator">
              <span class="poll-dot"></span>
              <span id="pollStatus">مزامنة مباشرة مع التطبيق</span>
            </div>
            """ : "")}}

            <div class="section-title">📋 بيانات الزائر</div>
            <div class="rows">
              <div class="row">
                <span class="lbl">اسم الزائر</span>
                <span class="val">{{visitorName}}</span>
              </div>
              <div class="row">
                <span class="lbl">نوع الزيارة</span>
                <span class="val">{{visitorType}}</span>
              </div>
              <div class="row">
                <span class="lbl">رقم الوحدة</span>
                <span class="val accent">{{unitId}}</span>
              </div>
              {{extraRows}}
              {{blacklistRow}}
              <div class="row">
                <span class="lbl">الحالة</span>
                <span id="statusChip"></span>
              </div>
              <div class="row">
                <span class="lbl">صالح حتى</span>
                <span class="val">{{expiryFormatted}}</span>
              </div>
            </div>

            <div class="action-result" id="actionResult"></div>
            {{actionButtons}}

            <div class="report-wrap">
              <div class="section-title" style="margin:0 0 10px 0;">🚨 إرسال بلاغ أمني</div>
              <textarea id="reportNotes" placeholder="اكتب تفاصيل البلاغ أو الملاحظة الأمنية…"></textarea>
              <select id="reportSeverity">
                <option value="INFO">معلومة</option>
                <option value="WARNING" selected>⚠️ تحذير</option>
                <option value="REJECTED">🚨 خطر</option>
              </select>
              <button class="btn-report" onclick="submitReport()">📤 إرسال البلاغ</button>
              <div id="reportMsg"></div>
            </div>

            <div class="security-note">🔒 هذه البيانات متاحة فقط لموظفي الأمن المصرح لهم</div>

            <div class="footer">
              <span>👤 {{empName}}</span>
              <span>🕐 {{nowFmt}}</span>
            </div>
          </div>

          <script>
            const VISIT_ID = {{visitIdStr}};
            const TOKEN    = '{{tokenJs}}';
            const EMP_NAME = '{{nameJs}}';
            let   pollTimer = null;
            let   currentStatus = '{{visitStatus}}';

            // ── chip renderer ────────────────────────────────────
            function getStatusChip(status) {
              const map = {
                'مقبولة':    ['chip-accepted','✅ مقبولة'],
                'داخل الآن':['chip-inside',  '🔵 داخل الآن'],
                'مرفوضة':   ['chip-rejected','🚫 مرفوضة'],
                'منتهية':    ['chip-expired', '⏰ منتهية'],
              };
              const [cls, lbl] = map[status] || ['chip-inside', status];
              return `<span class="status-chip ${cls}">${lbl}</span>`;
            }

            document.getElementById('statusChip').innerHTML = getStatusChip('{{visitStatus}}');

            // ── polling ──────────────────────────────────────────
            function startPolling() {
              if (pollTimer) return;
              pollTimer = setInterval(pollStatus, 4000);
            }

            async function pollStatus() {
              try {
                const res  = await fetch(`/api/Security/VisitStatus/${VISIT_ID}`);
                if (!res.ok) return;
                const data = await res.json();
                const newStatus = data.visit_status ?? '';
                if (newStatus === currentStatus) return;
                currentStatus = newStatus;
                onStatusChanged(newStatus);
              } catch {}
            }

            function onStatusChanged(newStatus) {
              document.getElementById('statusChip').innerHTML = getStatusChip(newStatus);

              const btnRow = document.getElementById('actionBtns');
              const resultEl = document.getElementById('actionResult');

              if (newStatus === 'داخل الآن') {
                // تحديث الواجهة: إخفاء أزرار الدخول وإظهار زر الخروج
                if (btnRow) {
                  btnRow.className = 'btn-row single';
                  btnRow.innerHTML = `
                    <button class="btn btn-exit" onclick="recordAction('exit')">
                      <span class="btn-icon">🚪</span>تسجيل خروج الزائر
                    </button>`;
                }
                document.getElementById('mainIcon').textContent = '🔵';
                document.getElementById('mainBadge').textContent = 'الزائر داخل المجمع';
                document.getElementById('mainBadge').style.borderColor = '#3B82F6';
                const ps = document.getElementById('pollStatus');
                if (ps) ps.textContent = 'الزائر داخل — بانتظار الخروج';
              }
              else if (newStatus === 'منتهية') {
                if (btnRow) btnRow.style.display = 'none';
                resultEl.className     = 'action-result ok';
                resultEl.textContent   = '🚪 تم تسجيل الخروج — انتهى التصريح';
                resultEl.style.display = 'block';
                document.getElementById('mainIcon').textContent = '⏰';
                document.getElementById('mainBadge').textContent = 'منتهي الصلاحية';
                document.getElementById('mainBadge').style.borderColor = '#F59E0B';
                stopPolling();
              }
              else if (newStatus === 'مرفوضة') {
                if (btnRow) btnRow.style.display = 'none';
                resultEl.className     = 'action-result err';
                resultEl.textContent   = '🚫 تم رفض الدخول';
                resultEl.style.display = 'block';
                document.getElementById('mainIcon').textContent = '🚫';
                document.getElementById('mainBadge').textContent = 'مرفوض';
                document.getElementById('mainBadge').style.borderColor = '#EF4444';
                stopPolling();
              }
            }

            function stopPolling() {
              if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
              const ind = document.querySelector('.poll-indicator');
              if (ind) { ind.querySelector('.poll-dot').style.animation = 'none'; ind.querySelector('.poll-dot').style.background = '#666'; }
            }

            // ── تسجيل الإجراء ───────────────────────────────────
            async function recordAction(type) {
              const btnRow   = document.getElementById('actionBtns');
              const resultEl = document.getElementById('actionResult');

              let endpoint, method = 'POST';
              if (type === 'entry')  endpoint = `RecordEntry/${VISIT_ID}`;
              else if (type === 'reject') endpoint = `RejectVisit/${VISIT_ID}`;
              else if (type === 'exit')  endpoint = `RecordExit/${VISIT_ID}`;

              const url = `/api/Security/${endpoint}?employeeCode=${encodeURIComponent(TOKEN)}&employeeFullName=${encodeURIComponent(EMP_NAME)}`;

              if (btnRow) btnRow.querySelectorAll('.btn').forEach(b => { b.disabled = true; b.textContent = 'جارٍ التنفيذ…'; });

              try {
                const res  = await fetch(url, { method });
                const data = await res.json();
                if (res.ok) {
                  const msgs = { entry:'✅ تم تسجيل الدخول بنجاح', reject:'🚫 تم رفض الدخول', exit:'🚪 تم تسجيل الخروج' };
                  resultEl.className     = 'action-result ok';
                  resultEl.textContent   = msgs[type] || '✅ تمت العملية';
                  resultEl.style.display = 'block';
                  // الواجهة ستتحدث تلقائياً عبر polling
                } else {
                  resultEl.className     = 'action-result err';
                  resultEl.textContent   = '❌ ' + (data.message || 'فشلت العملية');
                  resultEl.style.display = 'block';
                  if (btnRow) btnRow.querySelectorAll('.btn').forEach(b => b.disabled = false);
                  // إعادة النص الأصلي للأزرار
                  restoreBtnText(btnRow);
                }
              } catch {
                resultEl.className     = 'action-result err';
                resultEl.textContent   = '❌ خطأ في الاتصال بالسيرفر';
                resultEl.style.display = 'block';
                if (btnRow) { btnRow.querySelectorAll('.btn').forEach(b => b.disabled = false); restoreBtnText(btnRow); }
              }
            }

            function restoreBtnText(btnRow) {
              if (!btnRow) return;
              btnRow.querySelectorAll('.btn').forEach(b => {
                if (b.classList.contains('btn-accept')) b.innerHTML = '<span class="btn-icon">✅</span>قبول الدخول';
                else if (b.classList.contains('btn-reject')) b.innerHTML = '<span class="btn-icon">🚫</span>رفض الدخول';
                else if (b.classList.contains('btn-exit'))   b.innerHTML = '<span class="btn-icon">🚪</span>تسجيل خروج الزائر';
              });
            }

            // ── إرسال بلاغ ──────────────────────────────────────
            async function submitReport() {
              const notes    = document.getElementById('reportNotes').value.trim();
              const severity = document.getElementById('reportSeverity').value;
              const msgEl    = document.getElementById('reportMsg');
              if (!notes) { msgEl.className = 'err'; msgEl.style.display='block'; msgEl.textContent='❌ يرجى كتابة تفاصيل البلاغ'; return; }
              try {
                const res  = await fetch('/api/Security/SubmitReport', {
                  method: 'POST',
                  headers: { 'Content-Type':'application/json' },
                  body: JSON.stringify({ employeeCode:TOKEN, employeeFullName:EMP_NAME, visitId:VISIT_ID, notes, severity })
                });
                const data = await res.json();
                msgEl.className     = res.ok ? 'ok' : 'err';
                msgEl.style.display = 'block';
                msgEl.textContent   = res.ok ? '✅ ' + data.message : '❌ ' + data.message;
                if (res.ok) document.getElementById('reportNotes').value = '';
              } catch {
                msgEl.className = 'err'; msgEl.style.display='block'; msgEl.textContent='❌ خطأ في الاتصال';
              }
            }

            // ── بدء الـ polling إذا كانت الحالة تستدعي ذلك ──────
            if ({{(shouldPoll ? "true" : "false")}}) startPolling();
          </script>
        </body>
        </html>
        """;
        }

        // ══ DTOs ══════════════════════════════════════════════════════

        public class ScanResult
        {
            public bool authorized { get; set; }
            public string status { get; set; } = string.Empty;
            public string reason { get; set; } = string.Empty;
            public string employeeName { get; set; } = string.Empty;
            public string? blacklistReason { get; set; }

            /// <summary>
            /// ✅ كود الحالة الداخلي: APPROVED / EXPIRED / BLOCKED / ALREADY_IN / REJECTED
            /// يُستخدم من التطبيق لتمييز الحالات بدقة
            /// </summary>
            public string? statusCode { get; set; }

            public VisitDto? visit { get; set; }
        }

        public class VisitDto
        {
            public int? visit_id { get; set; }
            public string? visitor_name { get; set; }
            public string? visitor_type { get; set; }
            public string? car_number { get; set; }
            public int? unit_id { get; set; }
            public string? visit_status { get; set; }
            public DateTime? visit_date { get; set; }
            public DateTime? expiry_date { get; set; }
            public string? selected_days { get; set; }
            public string? morning_window { get; set; }
            public string? afternoon_window { get; set; }
        }

        public class SubmitReportRequest
        {
            public string? EmployeeCode { get; set; }
            public string? EmployeeFullName { get; set; }
            public int? VisitId { get; set; }
            public string? Notes { get; set; }
            public string? Severity { get; set; }
        }
    }
}