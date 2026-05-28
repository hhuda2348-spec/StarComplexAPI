using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;
using StarComplexAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace StarComplexAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BillsController : ControllerBase
    {
        private readonly StarComplexContext _context;

        public BillsController(StarComplexContext context)
        {
            _context = context;
        }

        // ══════════════════════════════════════════════════════════════
        // 1. جلب الفواتير — فقط البيانات الموجودة في قاعدة البيانات
        //    (مدفوعات مسجلة + طلبات صيانة) بدون أي بيانات افتراضية
        // ══════════════════════════════════════════════════════════════
        [HttpGet("GetUnitBills/{unitId}")]
        public async Task<IActionResult> GetUnitBills(int unitId)
        {
            try
            {
                var unitExists = await _context.HousingUnits.AnyAsync(u => u.unit_id == unitId);
                if (!unitExists)
                    return NotFound(new { message = "رقم الوحدة غير موجود" });

                // نوع الساكن
                var resident = await _context.Residents
                    .Where(r => r.unit_id == unitId)
                    .FirstOrDefaultAsync();

                string residentType = resident?.resident_type ?? "مؤجر";

                // كل الخدمات من قاعدة البيانات
                var allServices = await _context.FinancialConstants.ToListAsync();

                // المدفوعات المسجلة لهذه الوحدة فقط
                var payments = await _context.FinancialPayments
                    .Where(p => p.unit_id == unitId)
                    .ToListAsync();

                // طلبات الصيانة لهذه الوحدة فقط
                var maintenanceRequests = await _context.MaintenanceRequests
                    .Where(m => m.unit_id == unitId)
                    .ToListAsync();

                var bills = new List<BillItem>();

                // ── أ) الفواتير الشهرية المدفوعة فعلاً في قاعدة البيانات ──
                // نستثني المدفوعات المرتبطة بطلبات صيانة (تُعالج منفصلاً)
                foreach (var p in payments)
                {
                    bool isMaintenancePayment = maintenanceRequests
                        .Any(m => m.service_id == p.service_id &&
                                  m.request_date.Year == p.payment_date.Year &&
                                  m.request_date.Month == p.payment_date.Month);

                    if (isMaintenancePayment) continue;

                    var svc = allServices.FirstOrDefault(s => s.service_id == p.service_id);
                    if (svc == null) continue;

                    // تجاهل مدفوعات "كاش - بانتظار التأكيد" من قائمة المدفوعة
                    // (تُعرض كمستحقة لأن الموظف لم يؤكدها بعد)
                    if (p.payment_method == "كاش - بانتظار التأكيد") continue;

                    bills.Add(new BillItem
                    {
                        service_id = (int)p.service_id,
                        service_name = svc.service_name,
                        amount = (decimal)p.total_service_fee,
                        month = new DateTime(p.payment_date.Year, p.payment_date.Month, 1),
                        month_label = p.payment_date.ToString("MMMM yyyy"),
                        is_paid = true,
                        bill_type = "شهري",
                        payment_method = p.payment_method,
                        payment_date = p.payment_date
                    });
                }

                // ── ب) طلبات الصيانة — تظهر كفاتورة فقط إذا أبلغ الساكن بتنفيذها ─────
                foreach (var req in maintenanceRequests)
                {
                    // شرط أساسي: لا تُعرض فاتورة إلا إذا كانت الحالة "تم تنفيذه"
                    if (req.request_status != "تم تنفيذ الطلب") continue;

                    var svc = allServices.FirstOrDefault(s => s.service_id == req.service_id);
                    if (svc == null) continue;

                    // هل يوجد دفع مؤكد (غير كاش انتظار) لهذا الطلب في نفس الشهر؟
                    var matchedPayment = payments.FirstOrDefault(p =>
                        p.service_id == req.service_id &&
                        p.payment_date.Year == req.request_date.Year &&
                        p.payment_date.Month == req.request_date.Month &&
                        p.payment_method != "كاش - بانتظار التأكيد");

                    bool isPaid = matchedPayment != null;

                    bills.Add(new BillItem
                    {
                        service_id = req.service_id,
                        service_name = "صيانة: " + svc.service_name,
                        amount = (decimal)(isPaid ? matchedPayment!.total_service_fee : svc.service_price),
                        month = new DateTime(req.request_date.Year, req.request_date.Month, 1),
                        month_label = req.request_date.ToString("MMMM yyyy"),
                        is_paid = isPaid,
                        bill_type = "صيانة",
                        maintenance_status = req.request_status,
                        request_id = req.request_id,
                        payment_method = matchedPayment?.payment_method,
                        payment_date = matchedPayment?.payment_date
                    });
                }

                // ── ترتيب: غير المدفوعة أولاً ثم الأحدث ─────────────
                var sorted = bills
                    .OrderBy(b => b.is_paid)
                    .ThenByDescending(b => b.month)
                    .ToList();

                return Ok(new
                {
                    resident_type = residentType,
                    bills = sorted
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في جلب الفواتير", detail = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 2. جلب سجل المدفوعات السابقة
        // ══════════════════════════════════════════════════════════════
        [HttpGet("GetUnitPayments/{unitId}")]
        public async Task<IActionResult> GetUnitPayments(int unitId)
        {
            try
            {
                var payments = await _context.FinancialPayments
                    .Where(p => p.unit_id == unitId)
                    .Join(_context.FinancialConstants,
                        pay => pay.service_id,
                        con => con.service_id,
                        (pay, con) => new
                        {
                            payment_id = pay.payment_id,
                            service_id = pay.service_id,
                            total_service_fee = pay.total_service_fee,
                            payment_date = pay.payment_date,
                            payment_method = pay.payment_method,
                            service_name = con.service_name
                        })
                    .OrderByDescending(p => p.payment_date)
                    .ToListAsync();

                return Ok(payments);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في السجل المالي", detail = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 3. تسجيل دفعة إلكترونية
        // ══════════════════════════════════════════════════════════════
        [HttpPost("RecordPayment")]
        public async Task<IActionResult> RecordPayment([FromBody] RecordPaymentDto data)
        {
            if (data == null)
                return BadRequest(new { message = "البيانات غير مكتملة" });

            try
            {
                var unitExists = await _context.HousingUnits.AnyAsync(u => u.unit_id == data.UnitId);
                if (!unitExists)
                    return BadRequest(new { message = "رقم الوحدة غير موجود" });

                var newPayment = new FinancialPayment
                {
                    unit_id = data.UnitId,
                    service_id = data.ServiceId,
                    total_service_fee = data.Amount,
                    payment_date = DateTime.Now,
                    payment_method = data.Method,
                    employee_id = 0,
                    accont_received = data.Amount
                };

                _context.FinancialPayments.Add(newPayment);
                await _context.SaveChangesAsync();

                return Ok(new { status = "Success", message = "تم تسجيل الدفعة بنجاح" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "فشل تسجيل الدفعة", detail = ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 4. إشعار الدفع النقدي — يُنشئ بلاغاً في resident_reports
        //    ولا يغيّر حالة الفاتورة (صلاحية التأكيد للموظفين فقط)
        // ══════════════════════════════════════════════════════════════
        [HttpPost("NotifyCashPayment")]
        public async Task<IActionResult> NotifyCashPayment([FromBody] CashNotificationDto data)
        {
            if (data == null)
                return BadRequest(new { message = "البيانات غير مكتملة" });

            try
            {
                // جلب resident_id المرتبط بالوحدة
                var resident = await _context.Residents
                    .Where(r => r.unit_id == data.UnitId)
                    .FirstOrDefaultAsync();

                if (resident == null)
                    return BadRequest(new { message = "لا يوجد ساكن مسجل لهذه الوحدة" });

                // إنشاء بلاغ بنية resident_reports
                // القيم تطابق الـ ENUM في قاعدة البيانات
                var report = new ResidentReport
                {
                    resident_id = resident.resident_id,
                    title = $"طلب دفع كاش - {data.ServiceName}",
                    description = $"الساكن في الوحدة رقم {data.UnitId} يرغب في سداد فاتورة '{data.ServiceName}'" +
                                  $" عن شهر {data.MonthLabel} بمبلغ {data.Amount:N0} د.ع نقداً.\n" +
                                  $"يرجى مراجعة مكتب الإدارة لاستكمال الدفع وتأكيده.",
                    category = "مالي",
                    priority = "متوسط",   // ENUM: 'عالي' | 'متوسط' | 'عادي'
                    status = "قيد الانتظار",  // ENUM: 'قيد الانتظار' | 'جاري المعالجة' | 'تم الحل' | 'مرفوض'
                    created_at = DateTime.Now
                };

                _context.ResidentReport.Add(report);
                await _context.SaveChangesAsync();

                return Ok(new { status = "Success", message = "تم إرسال طلب الدفع النقدي للإدارة" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "فشل إرسال الطلب", detail = ex.Message });
            }
        }
    }

    // ══ موديل الفاتورة ════════════════════════════════════════════════
    public class BillItem
    {
        public int service_id { get; set; }
        public string service_name { get; set; } = "";
        public decimal amount { get; set; }
        public DateTime month { get; set; }
        public string month_label { get; set; } = "";
        public bool is_paid { get; set; }
        public string bill_type { get; set; } = "شهري";
        public int? request_id { get; set; }
        public string? maintenance_status { get; set; }
        public string? payment_method { get; set; }
        public DateTime? payment_date { get; set; }
    }

    // ══ DTOs ══════════════════════════════════════════════════════════
    public class RecordPaymentDto
    {
        [JsonPropertyName("unitId")] public int UnitId { get; set; }
        [JsonPropertyName("serviceId")] public int ServiceId { get; set; }
        [JsonPropertyName("amount")] public decimal Amount { get; set; }
        [JsonPropertyName("method")] public string Method { get; set; } = "";
    }

    public class CashNotificationDto
    {
        [JsonPropertyName("unitId")] public int UnitId { get; set; }
        [JsonPropertyName("serviceId")] public int ServiceId { get; set; }
        [JsonPropertyName("amount")] public decimal Amount { get; set; }
        [JsonPropertyName("monthLabel")] public string MonthLabel { get; set; } = "";
        [JsonPropertyName("serviceName")] public string ServiceName { get; set; } = "";
    }
}