using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Konscious.Security.Cryptography;
using System.Security.Cryptography;
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

        private const int Argon2MemorySize = 16384;
        private const int Argon2Iterations = 2;
        private const int Argon2Parallelism = 1;
        private const int Argon2HashLength = 32;
        private const string TypeSecurity = "security";

        public SecurityController(StarComplexContext context)
        {
            _context = context;
        }

        // ══════════════════════════════════════════════════════════
        //  HELPER — التحقق بـ Argon2id
        //  storedHash صيغة: "base64(salt):base64(hash)"
        //  combined   صيغة: "fullName||code"
        // ══════════════════════════════════════════════════════════
        private static bool VerifyPasswordHash(string fullName, string code, string storedHash)
        {
            try
            {
                string[] parts = storedHash.Split(':');
                if (parts.Length != 2) return false;

                byte[] salt = Convert.FromBase64String(parts[0]);
                byte[] storedBytes = Convert.FromBase64String(parts[1]);

                string combined = $"{fullName.Trim()}||{code.Trim()}";
                byte[] input = Encoding.UTF8.GetBytes(combined);

                using var argon2 = new Argon2id(input)
                {
                    Salt = salt,
                    MemorySize = Argon2MemorySize,
                    Iterations = Argon2Iterations,
                    DegreeOfParallelism = Argon2Parallelism
                };

                byte[] computed = argon2.GetBytes(Argon2HashLength);
                return CryptographicOperations.FixedTimeEquals(computed, storedBytes);
            }
            catch { return false; }
        }

        // ══════════════════════════════════════════════════════════
        //  HELPER — التحقق من موظف الأمن
        //  المعامل: employeeCode + employeeFullName (الاسم الثلاثي)
        // ══════════════════════════════════════════════════════════
        private async Task<Employee?> AuthEmployee(string? code, string? fullName)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(fullName))
                return null;

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e =>
                    (e.employee_code ?? "").Trim() == code.Trim() &&
                    e.employee_type == TypeSecurity);

            if (employee == null) return null;
            if (string.IsNullOrEmpty(employee.password_hash)) return null;

            return VerifyPasswordHash(fullName, code, employee.password_hash)
                ? employee
                : null;
        }

        // ══════════════════════════════════════════════════════════
        //  HELPER — كتابة سجل أمني
        // ══════════════════════════════════════════════════════════
        private async Task WriteLog(
            int employeeId,
            string actionType,
            string actionResult,
            string? notes,
            int? visitId = null,
            string? visitorSnapshot = null,
            int? unitSnapshot = null)
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
        //     ?employeeCode=CODE&employeeFullName=الاسم الثلاثي
        // ══════════════════════════════════════════════════════════
        [HttpGet("VerifyEmployee")]
        public async Task<IActionResult> VerifyEmployee(
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName)
        {
            var employee = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new { valid = false, message = "بيانات الموظف غير صحيحة" });

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
                employeeCode = employee.employee_code
            });
        }

        // ══════════════════════════════════════════════════════════
        //  2. فحص التصريح
        //     GET api/Security/ScanQR/{visitId}
        //     ?employeeCode=CODE&employeeFullName=الاسم الثلاثي
        // ══════════════════════════════════════════════════════════
        [HttpGet("ScanQR/{visitId}")]
        public async Task<IActionResult> ScanQR(
            int visitId,
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName)
        {
            var employee = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new
                {
                    authorized = false,
                    status = "رفض",
                    reason = "صلاحية غير كافية",
                    message = "بيانات الموظف غير صحيحة"
                });

            var visit = await _context.Visits
                .FirstOrDefaultAsync(v => v.visit_id == visitId);
            if (visit == null)
                return NotFound(new
                {
                    authorized = false,
                    status = "غير موجود",
                    reason = "التصريح لم يُعثر عليه في النظام",
                    message = "التصريح غير موجود"
                });

            string empName = string.Join(" ",
                new[] { employee.first_name, employee.second_name }
                .Where(n => !string.IsNullOrWhiteSpace(n)));

            // ── فحص انتهاء الصلاحية ─────────────────────────────
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

            // ── فحص القائمة السوداء ──────────────────────────────
            var blacklistEntry = await _context.Blacklist
                .Where(b => b.person_name != null &&
                            visit.visitor_name != null &&
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

            // ── مقبول ────────────────────────────────────────────
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
        //     ?employeeCode=CODE&employeeFullName=الاسم الثلاثي
        // ══════════════════════════════════════════════════════════
        [HttpPost("RecordEntry/{visitId}")]
        public async Task<IActionResult> RecordEntry(
            int visitId,
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName)
        {
            var employee = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new { message = "بيانات الموظف غير صحيحة" });

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
        //     ?employeeCode=CODE&employeeFullName=الاسم الثلاثي
        // ══════════════════════════════════════════════════════════
        [HttpPost("RecordExit/{visitId}")]
        public async Task<IActionResult> RecordExit(
            int visitId,
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName)
        {
            var employee = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new { message = "بيانات الموظف غير صحيحة" });

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
        //     body: { employeeCode, employeeFullName, visitId, notes, severity }
        // ══════════════════════════════════════════════════════════
        [HttpPost("SubmitReport")]
        public async Task<IActionResult> SubmitReport([FromBody] SubmitReportRequest req)
        {
            var employee = await AuthEmployee(req.EmployeeCode, req.EmployeeFullName);
            if (employee == null)
                return Unauthorized(new { message = "بيانات الموظف غير صحيحة" });

            if (string.IsNullOrWhiteSpace(req.Notes))
                return BadRequest(new { message = "يرجى كتابة تفاصيل البلاغ" });

            Visit? visit = null;
            if (req.VisitId.HasValue)
                visit = await _context.Visits.FindAsync(req.VisitId.Value);

            await WriteLog(
                employee.employee_id,
                "REPORT",
                req.Severity ?? "WARNING",
                req.Notes,
                req.VisitId,
                visit?.visitor_name,
                visit?.unit_id);

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
        //     ?employeeCode=CODE&employeeFullName=الاسم الثلاثي
        // ══════════════════════════════════════════════════════════
        [HttpGet("MyLogs")]
        public async Task<IActionResult> GetMyLogs(
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName,
            [FromQuery] int pageSize = 50)
        {
            var employee = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new { message = "بيانات الموظف غير صحيحة" });

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
        //     ?employeeCode=CODE&employeeFullName=الاسم الثلاثي
        // ══════════════════════════════════════════════════════════
        [HttpPost("Emergency")]
        public async Task<IActionResult> TriggerEmergency(
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName,
            [FromQuery] string? notes)
        {
            var employee = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new { message = "بيانات الموظف غير صحيحة" });

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
        //  9. صفحة QR
        //     GET api/Security/QRPage/{visitId}
        //     ?token=CODE&name=الاسم الثلاثي (URL-encoded)
        // ══════════════════════════════════════════════════════════
        [HttpGet("QRPage/{visitId}")]
        public async Task<IActionResult> QRPage(
            int visitId,
            [FromQuery] string? token,
            [FromQuery] string? name)
        {
            var employee = await AuthEmployee(token, name);
            if (employee == null)
                return Content(BuildBlankPage(), "text/html");

            var visit = await _context.Visits
                .FirstOrDefaultAsync(v => v.visit_id == visitId);
            if (visit == null)
                return Content(BuildErrorPage("التصريح غير موجود"), "text/html");

            var blacklistEntry = await _context.Blacklist
                .Where(b => b.person_name != null &&
                            visit.visitor_name != null &&
                            visit.visitor_name.Contains(b.person_name))
                .FirstOrDefaultAsync();

            bool expired = visit.expiry_date.HasValue &&
                           visit.expiry_date.Value < DateTime.Now;

            string approvalStatus = blacklistEntry != null ? "BLOCKED"
                                  : expired ? "EXPIRED"
                                  : "APPROVED";

            string empName = string.Join(" ",
                new[] { employee.first_name, employee.second_name }
                .Where(n => !string.IsNullOrWhiteSpace(n)));
            string blockReason = blacklistEntry?.reason ?? string.Empty;

            await WriteLog(
                employee.employee_id,
                "SCAN",
                approvalStatus == "APPROVED" ? "APPROVED" : "REJECTED",
                $"فتح صفحة التصريح #{visitId} — النتيجة: {approvalStatus}",
                visitId, visit.visitor_name, visit.unit_id);

            return Content(
                BuildQRPage(visit, approvalStatus, empName, blockReason, token!, name!),
                "text/html");
        }

        // ══════════════════════════════════════════════════════════
        //  ✅ 10. جلب حالة زيارة محددة — للـ polling من MAUI وصفحة الويب
        //      GET api/Security/VisitStatus/{visitId}
        //      لا يحتاج تحقق من الموظف — يُستخدم للإشعارات فقط
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
        //  ✅ 11. رفض زيارة — يُستدعى من صفحة الويب (زر الرفض)
        //      POST api/Security/RejectVisit/{visitId}
        //      ?employeeCode=CODE&employeeFullName=الاسم الثلاثي
        //      يغير visit_status إلى "مرفوضة" — يلتقطه polling في MAUI
        // ══════════════════════════════════════════════════════════
        [HttpPost("RejectVisit/{visitId}")]
        public async Task<IActionResult> RejectVisit(
            int visitId,
            [FromQuery] string? employeeCode,
            [FromQuery] string? employeeFullName)
        {
            var employee = await AuthEmployee(employeeCode, employeeFullName);
            if (employee == null)
                return Unauthorized(new { message = "بيانات الموظف غير صحيحة" });

            var visit = await _context.Visits.FindAsync(visitId);
            if (visit == null)
                return NotFound(new { message = "التصريح غير موجود" });

            // منع رفض زيارة داخل الآن أو منتهية
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

        // ══════════════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════════════
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
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8">
              <meta name="robots" content="noindex,nofollow">
              <style>html,body{margin:0;padding:0;background:#f5f5f5;height:100%;}</style>
            </head>
            <body></body>
            </html>
            """;

        private static string BuildErrorPage(string msg) =>
            $"<html dir='rtl'><head><meta charset='utf-8'><style>" +
            "body{font-family:sans-serif;display:flex;align-items:center;" +
            "justify-content:center;height:100vh;background:#1a1a2e;color:#fff;margin:0}" +
            $"</style></head><body><h2>{msg}</h2></body></html>";

        private static string BuildQRPage(
            Visit v,
            string status,
            string empName,
            string blockReason,
            string token,
            string name)
        {
            var (bgColor, icon, statusAr, cardBorder) = status switch
            {
                "APPROVED" => ("#0d2137", "✅", "مقبول — يُسمح بالدخول", "#22C55E"),
                "BLOCKED" => ("#1a0a0a", "🚫", "محظور — مرفوض أمنياً", "#EF4444"),
                _ => ("#1a1a0d", "⏰", "منتهي الصلاحية", "#F59E0B"),
            };

            string extraRows = v.visitor_type == "خط نقل طلاب"
                ? $"""
                   <tr><td>أيام الدوام</td><td>{v.selected_days ?? "—"}</td></tr>
                   <tr><td>وقت الذهاب</td><td>{v.morning_window ?? "—"}</td></tr>
                   <tr><td>وقت الإياب</td><td>{v.afternoon_window ?? "—"}</td></tr>
                   """
                : $"<tr><td>رقم السيارة</td><td>{v.car_number ?? "لا يوجد"}</td></tr>";

            string blacklistRow = status == "BLOCKED" && !string.IsNullOrEmpty(blockReason)
                ? $"<tr style='background:rgba(239,68,68,.1)'><td>سبب الحظر</td>" +
                  $"<td style='color:#EF4444'>{blockReason}</td></tr>"
                : "";

            string expiryFormatted = v.expiry_date.HasValue
                ? v.expiry_date.Value.ToString("yyyy/MM/dd HH:mm") : "—";

            // ✅ أزرار القبول والرفض — تغير visit_status في DB فيلتقطه polling في MAUI
            string actionButtons = status == "APPROVED"
                ? """
                  <div class="btn-row">
                    <button class="btn btn-entry"  onclick="recordAction('entry')">✅ قبول الدخول</button>
                    <button class="btn btn-reject" onclick="recordAction('reject')">🚫 رفض الدخول</button>
                  </div>
                  """
                : "";

            string reportSection = $$"""
                <div class="report-section">
                  <div class="report-title">📋 إرسال بلاغ أمني</div>
                  <textarea id="reportNotes" placeholder="اكتب تفاصيل البلاغ أو الملاحظة…"></textarea>
                  <select id="reportSeverity">
                    <option value="INFO">معلومة</option>
                    <option value="WARNING" selected>تحذير</option>
                    <option value="REJECTED">رفض / خطر</option>
                  </select>
                  <button class="btn btn-report" onclick="submitReport()">📤 إرسال البلاغ</button>
                  <div id="reportMsg" style="display:none;margin-top:8px;font-size:13px;"></div>
                </div>
                """;

            string visitIdStr = v.visit_id?.ToString() ?? "—";
            string visitorName = v.visitor_name ?? "—";
            string visitorType = v.visitor_type ?? "—";
            string unitId = v.unit_id?.ToString() ?? "—";
            string visitStatus = v.visit_status ?? "—";
            string nowFormatted = DateTime.Now.ToString("HH:mm yyyy/MM/dd");

            string nameJs = name.Replace("\\", "\\\\").Replace("'", "\\'");
            string tokenJs = token.Replace("\\", "\\\\").Replace("'", "\\'");

            return $$"""
                <!DOCTYPE html>
                <html dir="rtl" lang="ar">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                  <title>فحص التصريح #{{visitIdStr}}</title>
                  <link href="https://fonts.googleapis.com/css2?family=Tajawal:wght@400;700;900&display=swap" rel="stylesheet">
                  <style>
                    *,*::before,*::after{box-sizing:border-box;margin:0;padding:0}
                    body{font-family:'Tajawal',sans-serif;background:{{bgColor}};min-height:100vh;
                         display:flex;align-items:center;justify-content:center;padding:20px}
                    .card{background:rgba(255,255,255,.05);border:2px solid {{cardBorder}};
                          border-radius:24px;padding:36px 32px;max-width:500px;width:100%;
                          backdrop-filter:blur(12px);box-shadow:0 0 60px {{cardBorder}}33;
                          animation:fadeIn .5s ease}
                    @keyframes fadeIn{from{opacity:0;transform:translateY(20px)}to{opacity:1;transform:translateY(0)}
                    .icon{font-size:72px;text-align:center;margin-bottom:16px;animation:pulse 2s infinite}
                    @keyframes pulse{0%,100%{transform:scale(1)}50%{transform:scale(1.08)}
                    .status-badge{background:{{cardBorder}};color:#000;font-size:20px;font-weight:900;
                                  text-align:center;padding:12px;border-radius:12px;margin-bottom:20px}
                    .permit-id{color:#ffffff55;font-size:13px;text-align:center;margin-bottom:20px;letter-spacing:1px}
                    table{width:100%;border-collapse:collapse}
                    td{padding:11px 8px;font-size:15px;border-bottom:1px solid #ffffff11;color:#e0e0e0}
                    td:first-child{color:#ffffff88;font-size:13px;width:40%}
                    td:last-child{font-weight:700;color:#fff}
                    .divider{height:1px;background:#ffffff22;margin:20px 0}
                    .footer{margin-top:20px;text-align:center;color:#ffffff44;font-size:12px}
                    .btn-row{display:flex;gap:12px;margin-bottom:16px}
                    .btn{flex:1;padding:14px;border:none;border-radius:12px;
                         font-family:'Tajawal',sans-serif;font-size:15px;font-weight:700;
                         cursor:pointer;transition:.2s}
                    .btn-entry{background:#22C55E;color:#000}
                    .btn-reject{background:#EF4444;color:#fff}
                    .btn-report{background:#C9963A;color:#000;width:100%;margin-top:10px}
                    .btn:hover{opacity:.85;transform:scale(.98)}
                    .btn:disabled{opacity:.4;cursor:default;transform:none}
                    .action-result{padding:12px;border-radius:10px;text-align:center;
                                   font-size:14px;font-weight:700;display:none;margin-bottom:12px}
                    .action-result.ok{background:rgba(34,197,94,.15);color:#22C55E;border:1px solid rgba(34,197,94,.3)}
                    .action-result.err{background:rgba(239,68,68,.15);color:#EF4444;border:1px solid rgba(239,68,68,.3)}
                    .report-section{background:rgba(255,255,255,.04);border:1px solid rgba(255,255,255,.1);
                                    border-radius:14px;padding:16px;margin-top:4px}
                    .report-title{color:#C9963A;font-size:14px;font-weight:700;margin-bottom:10px}
                    #reportNotes{width:100%;height:80px;background:rgba(0,0,0,.3);
                                 border:1px solid rgba(255,255,255,.15);border-radius:10px;
                                 color:#fff;font-family:'Tajawal',sans-serif;font-size:14px;
                                 padding:10px;resize:none;outline:none}
                    #reportNotes::placeholder{color:#ffffff44}
                    #reportSeverity{width:100%;margin-top:8px;padding:8px 12px;
                                    background:rgba(0,0,0,.3);border:1px solid rgba(255,255,255,.15);
                                    border-radius:10px;color:#fff;font-family:'Tajawal',sans-serif;
                                    font-size:13px;outline:none}
                    .security-note{background:rgba(201,150,58,.2);border:1px solid rgba(201,150,58,.4);
                                   border-radius:10px;padding:12px;margin-top:16px;
                                   font-size:11px;color:#C9963A}
                  </style>
                </head>
                <body>
                  <div class="card">
                    <div class="icon">{{icon}}</div>
                    <div class="status-badge">{{statusAr}}</div>
                    <div class="permit-id">🔒 رقم التصريح: #{{visitIdStr}}</div>
                    <table>
                      <tr><td>اسم الزائر</td><td>{{visitorName}}</td></tr>
                      <tr><td>نوع الزيارة</td><td>{{visitorType}}</td></tr>
                      <tr><td>الوحدة</td>    <td>{{unitId}}</td></tr>
                      {{extraRows}}
                      {{blacklistRow}}
                      <tr><td>الحالة</td>    <td id="currentStatus">{{visitStatus}}</td></tr>
                      <tr><td>صالح حتى</td>  <td>{{expiryFormatted}}</td></tr>
                    </table>
                    <div class="security-note">
                      🔒 هذه البيانات متاحة فقط لموظفي الأمن المصرح لهم
                    </div>
                    <div class="divider"></div>
                    <div class="action-result" id="actionResult"></div>
                    {{actionButtons}}
                    {{reportSection}}
                    <div class="footer">فحص بواسطة: {{empName}} &nbsp;|&nbsp; {{nowFormatted}}</div>
                  </div>

                  <script>
                    const VISIT_ID = {{visitIdStr}};
                    const TOKEN    = '{{tokenJs}}';
                    const EMP_NAME = '{{nameJs}}';   // الاسم الثلاثي الكامل

                    // ✅ قبول أو رفض — يغير visit_status في DB
                    //    الـ polling في MAUI يلتقط التغيير تلقائياً
                    async function recordAction(type) {
                      const isEntry  = type === 'entry';
                      const endpoint = isEntry ? 'RecordEntry' : 'RejectVisit';
                      const url      = `/api/Security/${endpoint}/${VISIT_ID}`
                                     + `?employeeCode=${encodeURIComponent(TOKEN)}`
                                     + `&employeeFullName=${encodeURIComponent(EMP_NAME)}`;

                      const btnRow   = document.querySelector('.btn-row');
                      const resultEl = document.getElementById('actionResult');
                      if (btnRow) btnRow.querySelectorAll('.btn').forEach(b => {
                        b.disabled    = true;
                        b.textContent = 'جارٍ التنفيذ…';
                      });

                      try {
                        const res  = await fetch(url, { method: 'POST' });
                        const data = await res.json();

                        if (res.ok) {
                          resultEl.className   = 'action-result ok';
                          resultEl.textContent = isEntry
                            ? '✅ تم قبول الزيارة — سيتم إشعار موظف الأمن تلقائياً'
                            : '🚫 تم رفض الزيارة — سيتم إشعار موظف الأمن تلقائياً';
                          resultEl.style.display = 'block';
                          if (btnRow) btnRow.style.display = 'none';
                          // تحديث الحالة في الجدول
                          const statusEl = document.getElementById('currentStatus');
                          if (statusEl) statusEl.textContent = isEntry ? 'مقبولة' : 'مرفوضة';
                        } else {
                          resultEl.className     = 'action-result err';
                          resultEl.textContent   = '❌ ' + (data.message || 'فشلت العملية');
                          resultEl.style.display = 'block';
                          if (btnRow) btnRow.querySelectorAll('.btn').forEach(b => {
                            b.disabled = false;
                          });
                          // استعادة نصوص الأزرار
                          const entryBtn  = document.querySelector('.btn-entry');
                          const rejectBtn = document.querySelector('.btn-reject');
                          if (entryBtn)  entryBtn.textContent  = '✅ قبول الدخول';
                          if (rejectBtn) rejectBtn.textContent = '🚫 رفض الدخول';
                        }
                      } catch {
                        resultEl.className     = 'action-result err';
                        resultEl.textContent   = '❌ خطأ في الاتصال بالسيرفر';
                        resultEl.style.display = 'block';
                        if (btnRow) btnRow.querySelectorAll('.btn').forEach(b => {
                          b.disabled = false;
                        });
                        const entryBtn  = document.querySelector('.btn-entry');
                        const rejectBtn = document.querySelector('.btn-reject');
                        if (entryBtn)  entryBtn.textContent  = '✅ قبول الدخول';
                        if (rejectBtn) rejectBtn.textContent = '🚫 رفض الدخول';
                      }
                    }

                    async function submitReport() {
                      const notes    = document.getElementById('reportNotes').value.trim();
                      const severity = document.getElementById('reportSeverity').value;
                      const msgEl    = document.getElementById('reportMsg');
                      if (!notes) {
                        msgEl.style.color   = '#EF4444';
                        msgEl.style.display = 'block';
                        msgEl.textContent   = 'يرجى كتابة تفاصيل البلاغ';
                        return;
                      }
                      try {
                        const res  = await fetch('/api/Security/SubmitReport', {
                          method:  'POST',
                          headers: { 'Content-Type': 'application/json' },
                          body: JSON.stringify({
                            employeeCode:     TOKEN,
                            employeeFullName: EMP_NAME,
                            visitId:          VISIT_ID,
                            notes,
                            severity
                          })
                        });
                        const data = await res.json();
                        msgEl.style.color   = res.ok ? '#22C55E' : '#EF4444';
                        msgEl.style.display = 'block';
                        msgEl.textContent   = res.ok
                          ? '✅ ' + data.message
                          : '❌ ' + data.message;
                        if (res.ok)
                          document.getElementById('reportNotes').value = '';
                      } catch {
                        msgEl.style.color   = '#EF4444';
                        msgEl.style.display = 'block';
                        msgEl.textContent   = '❌ خطأ في الاتصال بالسيرفر';
                      }
                    }
                  </script>
                </body>
                </html>
                """;
        }
    }

    // ══ DTOs & Models ═════════════════════════════════════════════

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
        public string? EmployeeFullName { get; set; }   // ✅ الاسم الثلاثي الكامل
        public int? VisitId { get; set; }
        public string? Notes { get; set; }
        public string? Severity { get; set; }
    }
}