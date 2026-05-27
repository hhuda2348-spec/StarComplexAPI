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
        //  يقبل: امن / security / موظف امن  (job_title أو employee_type)
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
        //  HELPER — التحقق من موظف الأمن بدون هاش
        //  مقارنة مباشرة للاسم + التحقق من أنه موظف أمن
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

            // مقارنة الاسم مع قاعدة البيانات
            var dbFullName = NormaliseArabic(string.Join(" ",
                new[] { employee.first_name, employee.second_name, employee.third_name }
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!.Trim())));

            if (!string.Equals(dbFullName, cleanName, StringComparison.Ordinal))
                return (null, "بيانات الموظف غير صحيحة");

            // التحقق من أنه موظف أمن
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

            // فحص انتهاء الصلاحية
            if (visit.expiry_date.HasValue && visit.expiry_date.Value < DateTime.Now)
            {
                visit.visit_status = "منتهية";
                await _context.SaveChangesAsync();
                await WriteLog(employee.employee_id, "SCAN", "REJECTED",
                    "تصريح منتهي الصلاحية", visitId, visit.visitor_name, visit.unit_id);

                return Ok(new ScanResult
                {
                    authorized = false,
                    status = "منتهية",
                    reason = "انتهت صلاحية هذا التصريح",
                    visit = MapVisit(visit),
                    employeeName = empName
                });
            }

            // فحص القائمة السوداء
            var blacklistEntry = await _context.Blacklist
                .Where(b => b.person_name != null && visit.visitor_name != null &&
                            visit.visitor_name.Contains(b.person_name))
                .FirstOrDefaultAsync();

            if (blacklistEntry != null)
            {
                await WriteLog(employee.employee_id, "BLACKLIST_HIT", "REJECTED",
                    $"محاولة دخول شخص محظور — السبب: {blacklistEntry.reason}",
                    visitId, visit.visitor_name, visit.unit_id);

                return Ok(new ScanResult
                {
                    authorized = false,
                    status = "محظور",
                    reason = "الشخص مدرج في القائمة السوداء",
                    visit = MapVisit(visit),
                    employeeName = empName,
                    blacklistReason = blacklistEntry.reason
                });
            }

            await WriteLog(employee.employee_id, "SCAN", "APPROVED",
                "فحص تصريح — مقبول ✅", visitId, visit.visitor_name, visit.unit_id);

            return Ok(new ScanResult
            {
                authorized = true,
                status = visit.visit_status ?? "مقبولة",
                reason = "التصريح صالح ومصرح به",
                visit = MapVisit(visit),
                employeeName = empName
            });
        }

        // ══════════════════════════════════════════════════════════
        //  3. تسجيل الدخول
        //     POST api/Security/RecordEntry/{visitId}
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

            visit.visit_status = "داخل الآن";
            await _context.SaveChangesAsync();
            await WriteLog(employee.employee_id, "ENTRY", "APPROVED",
                "تم تسجيل دخول الزائر ✅", visitId, visit.visitor_name, visit.unit_id);

            return Ok(new
            {
                message = "تم تسجيل الدخول",
                entryTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                visitId
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
                visitId
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

            bool expired = visit.expiry_date.HasValue &&
                           visit.expiry_date.Value < DateTime.Now;

            string approvalStatus = blacklistEntry != null ? "BLOCKED"
                                  : expired ? "EXPIRED" : "APPROVED";

            string empName = string.Join(" ",
                new[] { employee.first_name, employee.second_name }
                .Where(n => !string.IsNullOrWhiteSpace(n)));
            string blockReason = blacklistEntry?.reason ?? string.Empty;

            await WriteLog(employee.employee_id, "SCAN",
                approvalStatus == "APPROVED" ? "APPROVED" : "REJECTED",
                $"فتح صفحة التصريح #{visitId} — النتيجة: {approvalStatus}",
                visitId, visit.visitor_name, visit.unit_id);

            return Content(
                BuildQRPage(visit, approvalStatus, empName, blockReason, token!, name!),
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

        private static string BuildQRPage(
            Visit v, string status, string empName,
            string blockReason, string token, string name)
        {
            var (bgGradient, accentColor, statusIcon, statusAr) = status switch
            {
                "APPROVED" => ("135deg,#0a1628 0%,#0d2137 100%", "#22C55E", "✅", "مقبول — يُسمح بالدخول"),
                "BLOCKED" => ("135deg,#1a0505 0%,#2d0a0a 100%", "#EF4444", "🚫", "محظور — مرفوض أمنياً"),
                _ => ("135deg,#1a1505 0%,#2d2205 100%", "#F59E0B", "⏰", "منتهي الصلاحية"),
            };

            string extraRows = v.visitor_type == "خط نقل طلاب"
                ? $"""
                   <div class="row"><span class="lbl">أيام الدوام</span><span class="val">{v.selected_days ?? "—"}</span></div>
                   <div class="row"><span class="lbl">وقت الذهاب</span><span class="val">{v.morning_window ?? "—"}</span></div>
                   <div class="row"><span class="lbl">وقت الإياب</span><span class="val">{v.afternoon_window ?? "—"}</span></div>
                   """
                : $"""<div class="row"><span class="lbl">رقم السيارة</span><span class="val car">🚗 {v.car_number ?? "لا يوجد"}</span></div>""";

            string blacklistRow = status == "BLOCKED" && !string.IsNullOrEmpty(blockReason)
                ? $"""<div class="row danger"><span class="lbl">سبب الحظر</span><span class="val" style="color:#EF4444">{blockReason}</span></div>"""
                : "";

            string expiryFormatted = v.expiry_date.HasValue
                ? v.expiry_date.Value.ToString("yyyy/MM/dd HH:mm") : "—";

            string actionButtons = status == "APPROVED" ? """
                <div class="btn-row">
                  <button class="btn btn-accept" onclick="recordAction('entry')">
                    <span class="btn-icon">✅</span>قبول الدخول
                  </button>
                  <button class="btn btn-reject" onclick="recordAction('reject')">
                    <span class="btn-icon">🚫</span>رفض الدخول
                  </button>
                </div>
                """ : "";

            string visitIdStr = v.visit_id?.ToString() ?? "—";
            string visitorName = v.visitor_name ?? "—";
            string visitorType = v.visitor_type ?? "—";
            string unitId = v.unit_id?.ToString() ?? "—";
            string visitStatus = v.visit_status ?? "—";
            string nowFmt = DateTime.Now.ToString("HH:mm  yyyy/MM/dd");
            string nameJs = name.Replace("\\", "\\\\").Replace("'", "\\'");
            string tokenJs = token.Replace("\\", "\\\\").Replace("'", "\\'");

            return $$"""
                <!DOCTYPE html>
                <html dir="rtl" lang="ar">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width,initial-scale=1">
                  <title>تصريح #{{visitIdStr}}</title>
                  <link href="https://fonts.googleapis.com/css2?family=Tajawal:wght@400;600;700;900&display=swap" rel="stylesheet">
                  <style>
                    *,*::before,*::after{box-sizing:border-box;margin:0;padding:0}
                    :root{
                      --accent:{{accentColor}};
                      --bg:linear-gradient({{bgGradient}});
                      --card:rgba(255,255,255,.04);
                      --border:rgba(255,255,255,.08);
                      --text:#E2EAF4;
                      --muted:#6B7E9A;
                    }
                    body{
                      font-family:'Tajawal',sans-serif;
                      background:var(--bg);
                      min-height:100vh;
                      display:flex;align-items:center;justify-content:center;
                      padding:20px;
                    }
                    .card{
                      width:100%;max-width:440px;
                      background:rgba(14,22,32,.95);
                      border:1px solid var(--accent);
                      border-radius:24px;
                      overflow:hidden;
                      box-shadow:0 0 60px color-mix(in srgb,var(--accent) 20%,transparent),
                                 0 20px 60px rgba(0,0,0,.5);
                      animation:slideUp .5s cubic-bezier(.34,1.56,.64,1);
                    }
                    @keyframes slideUp{from{opacity:0;transform:translateY(30px)}to{opacity:1;transform:translateY(0)}

                    .header{
                      background:linear-gradient(135deg,
                        color-mix(in srgb,var(--accent) 15%,transparent),
                        color-mix(in srgb,var(--accent) 5%,transparent));
                      border-bottom:1px solid color-mix(in srgb,var(--accent) 30%,transparent);
                      padding:28px 24px 22px;
                      text-align:center;
                      position:relative;
                    }
                    .header::before{
                      content:'';position:absolute;inset:0;
                      background:radial-gradient(ellipse at 50% 0%,
                        color-mix(in srgb,var(--accent) 12%,transparent) 0%,transparent 70%);
                    }
                    .status-icon{font-size:56px;display:block;margin-bottom:12px;
                                 filter:drop-shadow(0 0 16px var(--accent));
                                 animation:iconPulse 2.5s ease-in-out infinite}
                    @keyframes iconPulse{0%,100%{transform:scale(1)}50%{transform:scale(1.06)}
                    .status-badge{
                      display:inline-block;
                      background:color-mix(in srgb,var(--accent) 15%,transparent);
                      color:var(--accent);
                      border:1px solid color-mix(in srgb,var(--accent) 40%,transparent);
                      padding:8px 22px;border-radius:50px;
                      font-size:15px;font-weight:700;
                      letter-spacing:.5px;
                    }
                    .permit-id{
                      margin-top:10px;font-size:12px;color:var(--muted);
                      letter-spacing:1.5px;text-transform:uppercase;
                    }
                    .body{padding:20px 24px 24px}
                    .section-title{
                      font-size:11px;font-weight:700;color:var(--muted);
                      letter-spacing:1.5px;text-transform:uppercase;
                      margin:0 0 12px;padding-bottom:8px;
                      border-bottom:1px solid var(--border);
                    }
                    .rows{display:flex;flex-direction:column;gap:1px;margin-bottom:20px}
                    .row{
                      display:flex;justify-content:space-between;align-items:center;
                      padding:11px 14px;
                      background:var(--card);
                      border-radius:10px;
                      transition:.15s;
                    }
                    .row:hover{background:rgba(255,255,255,.06)}
                    .row.danger{background:rgba(239,68,68,.08)}
                    .lbl{font-size:12px;color:var(--muted)}
                    .val{font-size:13px;font-weight:700;color:var(--text)}
                    .val.accent{color:var(--accent)}
                    .val.car{font-family:monospace;letter-spacing:1px}
                    .status-chip{
                      display:inline-flex;align-items:center;gap:6px;
                      padding:4px 12px;border-radius:20px;font-size:12px;font-weight:700;
                    }
                    .chip-accepted{background:rgba(34,197,94,.12);color:#22C55E;border:1px solid rgba(34,197,94,.3)}
                    .chip-inside  {background:rgba(59,130,246,.12);color:#3B82F6;border:1px solid rgba(59,130,246,.3)}
                    .chip-rejected{background:rgba(239,68,68,.12);color:#EF4444;border:1px solid rgba(239,68,68,.3)}
                    .chip-expired {background:rgba(245,158,11,.12);color:#F59E0B;border:1px solid rgba(245,158,11,.3)}
                    .chip-default {background:rgba(255,255,255,.06);color:var(--text);border:1px solid var(--border)}
                    .divider{height:1px;background:var(--border);margin:4px 0 20px}
                    .action-result{
                      padding:12px 16px;border-radius:12px;
                      text-align:center;font-size:14px;font-weight:700;
                      display:none;margin-bottom:16px;
                      animation:fadeIn .3s ease;
                    }
                    @keyframes fadeIn{from{opacity:0}to{opacity:1}
                    .action-result.ok {background:rgba(34,197,94,.1);color:#22C55E;border:1px solid rgba(34,197,94,.25)}
                    .action-result.err{background:rgba(239,68,68,.1);color:#EF4444;border:1px solid rgba(239,68,68,.25)}
                    .btn-row{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-bottom:16px}
                    .btn{
                      height:52px;border:none;border-radius:14px;
                      font-family:'Tajawal',sans-serif;font-size:15px;font-weight:700;
                      cursor:pointer;transition:.2s;display:flex;align-items:center;
                      justify-content:center;gap:8px;
                    }
                    .btn-icon{font-size:18px}
                    .btn-accept{background:#22C55E;color:#000;box-shadow:0 4px 20px rgba(34,197,94,.25)}
                    .btn-accept:hover:not(:disabled){background:#16A34A;transform:translateY(-2px);box-shadow:0 6px 28px rgba(34,197,94,.4)}
                    .btn-reject{background:#EF4444;color:#fff;box-shadow:0 4px 20px rgba(239,68,68,.25)}
                    .btn-reject:hover:not(:disabled){background:#DC2626;transform:translateY(-2px);box-shadow:0 6px 28px rgba(239,68,68,.4)}
                    .btn:disabled{opacity:.35;cursor:not-allowed;transform:none!important}
                    .report-wrap{
                      background:rgba(201,150,58,.05);
                      border:1px solid rgba(201,150,58,.2);
                      border-radius:16px;padding:18px;
                    }
                    .report-title{font-size:13px;font-weight:700;color:#C9963A;margin-bottom:12px;
                                  display:flex;align-items:center;gap:6px}
                    #reportNotes{
                      width:100%;height:84px;
                      background:rgba(0,0,0,.3);
                      border:1px solid rgba(255,255,255,.1);
                      border-radius:10px;color:var(--text);
                      font-family:'Tajawal',sans-serif;font-size:13px;
                      padding:10px 12px;resize:none;outline:none;
                      transition:.2s;
                    }
                    #reportNotes:focus{border-color:#C9963A;box-shadow:0 0 0 3px rgba(201,150,58,.12)}
                    #reportNotes::placeholder{color:var(--muted)}
                    #reportSeverity{
                      width:100%;margin-top:8px;padding:9px 12px;
                      background:rgba(0,0,0,.3);border:1px solid rgba(255,255,255,.1);
                      border-radius:10px;color:var(--text);
                      font-family:'Tajawal',sans-serif;font-size:13px;outline:none;
                    }
                    .btn-report{
                      width:100%;height:46px;margin-top:10px;
                      border:none;border-radius:12px;
                      background:linear-gradient(135deg,#C9963A,#A67828);
                      color:#fff;font-family:'Tajawal',sans-serif;
                      font-size:14px;font-weight:700;cursor:pointer;transition:.2s;
                    }
                    .btn-report:hover{opacity:.9;transform:translateY(-1px)}
                    #reportMsg{font-size:12px;margin-top:8px;display:none;
                               text-align:center;font-weight:700;padding:8px;border-radius:8px}
                    #reportMsg.ok {background:rgba(34,197,94,.1);color:#22C55E}
                    #reportMsg.err{background:rgba(239,68,68,.1);color:#EF4444}
                    .footer{
                      margin-top:20px;padding-top:16px;
                      border-top:1px solid var(--border);
                      display:flex;justify-content:space-between;
                      font-size:11px;color:var(--muted);
                    }
                    .security-note{
                      margin-top:12px;padding:8px 12px;
                      background:rgba(201,150,58,.05);
                      border:1px solid rgba(201,150,58,.15);
                      border-radius:8px;font-size:11px;color:#C9963A;
                      text-align:center;
                    }
                  </style>
                </head>
                <body>
                  <div class="card">
                    <div class="header">
                      <span class="status-icon">{{statusIcon}}</span>
                      <div class="status-badge">{{statusAr}}</div>
                      <div class="permit-id">🔐 رقم التصريح: #{{visitIdStr}}</div>
                    </div>
                    <div class="body">
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
                          <span id="statusChip">{{visitStatus}}</span>
                        </div>
                        <div class="row">
                          <span class="lbl">صالح حتى</span>
                          <span class="val">{{expiryFormatted}}</span>
                        </div>
                      </div>
                      <div class="divider"></div>
                      <div class="action-result" id="actionResult"></div>
                      {{actionButtons}}
                      <div class="report-wrap">
                        <div class="report-title">📋 إرسال بلاغ أمني</div>
                        <textarea id="reportNotes" placeholder="اكتب تفاصيل البلاغ أو الملاحظة الأمنية…"></textarea>
                        <select id="reportSeverity">
                          <option value="INFO">معلومة</option>
                          <option value="WARNING" selected>⚠️ تحذير</option>
                          <option value="REJECTED">🚨 خطر</option>
                        </select>
                        <button class="btn-report" onclick="submitReport()">📤 إرسال البلاغ</button>
                        <div id="reportMsg"></div>
                      </div>
                      <div class="security-note">
                        🔒 هذه البيانات متاحة فقط لموظفي الأمن المصرح لهم
                      </div>
                      <div class="footer">
                        <span>👤 {{empName}}</span>
                        <span>🕐 {{nowFmt}}</span>
                      </div>
                    </div>
                  </div>
                  <script>
                    const VISIT_ID = {{visitIdStr}};
                    const TOKEN    = '{{tokenJs}}';
                    const EMP_NAME = '{{nameJs}}';

                    function getStatusChip(status) {
                      const map = {
                        'مقبولة':    ['chip-accepted','✅ مقبولة'],
                        'داخل الآن':['chip-inside','🔵 داخل الآن'],
                        'مرفوضة':   ['chip-rejected','🚫 مرفوضة'],
                        'منتهية':    ['chip-expired','⏰ منتهية'],
                      };
                      const [cls, lbl] = map[status] || ['chip-default', status];
                      return `<span class="status-chip ${cls}">${lbl}</span>`;
                    }

                    document.getElementById('statusChip').innerHTML =
                      getStatusChip('{{visitStatus}}');

                    async function recordAction(type) {
                      const isEntry  = type === 'entry';
                      const endpoint = isEntry ? 'RecordEntry' : 'RejectVisit';
                      const url      = `/api/Security/${endpoint}/${VISIT_ID}`
                                     + `?employeeCode=${encodeURIComponent(TOKEN)}`
                                     + `&employeeFullName=${encodeURIComponent(EMP_NAME)}`;

                      const btnRow   = document.querySelector('.btn-row');
                      const resultEl = document.getElementById('actionResult');
                      if (btnRow) btnRow.querySelectorAll('.btn')
                        .forEach(b => { b.disabled = true; b.textContent = 'جارٍ التنفيذ…'; });

                      try {
                        const res  = await fetch(url, { method: 'POST' });
                        const data = await res.json();
                        if (res.ok) {
                          resultEl.className   = 'action-result ok';
                          resultEl.textContent = isEntry
                            ? '✅ تم قبول الزيارة — سيُشعر موظف الأمن تلقائياً'
                            : '🚫 تم رفض الزيارة — سيُشعر موظف الأمن تلقائياً';
                          resultEl.style.display = 'block';
                          if (btnRow) btnRow.style.display = 'none';
                          document.getElementById('statusChip').innerHTML =
                            getStatusChip(isEntry ? 'مقبولة' : 'مرفوضة');
                        } else {
                          resultEl.className     = 'action-result err';
                          resultEl.textContent   = '❌ ' + (data.message || 'فشلت العملية');
                          resultEl.style.display = 'block';
                          if (btnRow) btnRow.querySelectorAll('.btn').forEach(b => {
                            b.disabled = false;
                            b.innerHTML = b.classList.contains('btn-accept')
                              ? '<span class="btn-icon">✅</span>قبول الدخول'
                              : '<span class="btn-icon">🚫</span>رفض الدخول';
                          });
                        }
                      } catch {
                        resultEl.className     = 'action-result err';
                        resultEl.textContent   = '❌ خطأ في الاتصال بالسيرفر';
                        resultEl.style.display = 'block';
                        if (btnRow) btnRow.querySelectorAll('.btn').forEach(b => {
                          b.disabled = false;
                          b.innerHTML = b.classList.contains('btn-accept')
                            ? '<span class="btn-icon">✅</span>قبول الدخول'
                            : '<span class="btn-icon">🚫</span>رفض الدخول';
                        });
                      }
                    }

                    async function submitReport() {
                      const notes    = document.getElementById('reportNotes').value.trim();
                      const severity = document.getElementById('reportSeverity').value;
                      const msgEl    = document.getElementById('reportMsg');
                      if (!notes) {
                        msgEl.className = 'err'; msgEl.style.display = 'block';
                        msgEl.textContent = '❌ يرجى كتابة تفاصيل البلاغ'; return;
                      }
                      try {
                        const res  = await fetch('/api/Security/SubmitReport', {
                          method: 'POST',
                          headers: { 'Content-Type': 'application/json' },
                          body: JSON.stringify({
                            employeeCode: TOKEN, employeeFullName: EMP_NAME,
                            visitId: VISIT_ID, notes, severity
                          })
                        });
                        const data = await res.json();
                        msgEl.className     = res.ok ? 'ok' : 'err';
                        msgEl.style.display = 'block';
                        msgEl.textContent   = res.ok ? '✅ ' + data.message : '❌ ' + data.message;
                        if (res.ok) document.getElementById('reportNotes').value = '';
                      } catch {
                        msgEl.className = 'err'; msgEl.style.display = 'block';
                        msgEl.textContent = '❌ خطأ في الاتصال';
                      }
                    }
                  </script>
                </body>
                </html>
                """;
        }
    }

    // ══ DTOs ══════════════════════════════════════════════════════

    public class ScanResult
    {
        public bool authorized { get; set; }
        public string status { get; set; } = string.Empty;
        public string reason { get; set; } = string.Empty;
        public string employeeName { get; set; } = string.Empty;
        public string? blacklistReason { get; set; }
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
