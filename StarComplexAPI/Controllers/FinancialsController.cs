using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;
using StarComplexAPI.Models;

namespace StarComplexAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FinancialsController : ControllerBase
    {
        private readonly StarComplexContext _context;
        public FinancialsController(StarComplexContext context) => _context = context;

        // ─────────────────────────────────────────────
        // GET /api/Financials/summary
        // ─────────────────────────────────────────────
        [HttpGet("summary")]
        public async Task<ActionResult<FinancialSummaryDto>> GetSummary()
        {
            decimal totalRevenue = await _context.FinancialPayments
                .SumAsync(p => p.total_service_fee);

            var now = DateTime.Now;
            var currentMonth = now.Month;
            var currentYear = now.Year;

            // الوحدات المشغولة
            var occupiedUnitIds = await _context.HousingUnits
                .Where(u => u.unit_status == "مشغول")
                .Select(u => u.unit_id)
                .ToListAsync();

            // الوحدات التي دفعت إيجار هذا الشهر (service_id = 5)
            var paidThisMonth = await _context.FinancialPayments
                .Where(p => p.payment_date.Month == currentMonth
                         && p.payment_date.Year == currentYear
                         && p.service_id == 5)
                .Select(p => p.unit_id)
                .Distinct()
                .ToListAsync();

            decimal monthlyRent = await _context.FinancialConstants
                .Where(s => s.service_id == 5)
                .Select(s => s.service_price)
                .FirstOrDefaultAsync();

            // مستحقات الشهر الحالي
            decimal outstandingAmount =
                occupiedUnitIds.Except(paidThisMonth).Count() * monthlyRent;

            // متأخرات: وحدات لم تدفع خلال آخر 3 أشهر
            var threeMonthsAgo = now.AddMonths(-3);
            var paidLast3M = await _context.FinancialPayments
                .Where(p => p.payment_date >= threeMonthsAgo && p.service_id == 5)
                .Select(p => p.unit_id)
                .Distinct()
                .ToListAsync();

            decimal lateAmount =
                occupiedUnitIds.Except(paidLast3M).Count() * monthlyRent * 3;

            // المتوسط الشهري
            int monthsWithPayments = await _context.FinancialPayments
                .Select(p => new { p.payment_date.Year, p.payment_date.Month })
                .Distinct()
                .CountAsync();

            decimal monthlyAvg = monthsWithPayments > 0
                ? totalRevenue / monthsWithPayments : 0;

            return Ok(new FinancialSummaryDto
            {
                TotalRevenue = $"{totalRevenue:N0} IQD",
                OutstandingAmount = $"{outstandingAmount:N0} IQD",
                LateAmount = $"{lateAmount:N0} IQD",
                MonthlyAvg = $"{monthlyAvg:N0} IQD"
            });
        }

        // ─────────────────────────────────────────────
        // GET /api/Financials/services
        // يعيد محتوى جدول financial_constants كاملاً
        // ─────────────────────────────────────────────
        [HttpGet("services")]
        public async Task<ActionResult<List<ServiceDto>>> GetServices()
        {
            var services = await _context.FinancialConstants
                .OrderBy(s => s.service_id)
                .Select(s => new ServiceDto
                {
                    ServiceId = s.service_id,
                    ServiceName = s.service_name,
                    ServicePrice = s.service_price
                })
                .ToListAsync();

            return Ok(services);
        }

        // ─────────────────────────────────────────────
        // GET /api/Financials/recent
        //
        // يعيد آخر 100 دفعة مع اسم الموظف المنشئ لها
        // عبر JOIN على employee_id المحفوظ في financial_payments
        // ─────────────────────────────────────────────
        [HttpGet("recent")]
        public async Task<ActionResult<List<PaymentItemDto>>> GetRecentPayments(
            [FromQuery] string? method = null,
            [FromQuery] int? unitId = null,
            [FromQuery] string? status = null)
        {
            var paymentsQuery = _context.FinancialPayments.AsQueryable();

            if (!string.IsNullOrWhiteSpace(method))
                paymentsQuery = paymentsQuery.Where(p => p.payment_method == method);

            if (unitId.HasValue)
                paymentsQuery = paymentsQuery.Where(p => p.unit_id == unitId.Value);

            // JOIN: financial_payments ← financial_constants ← employees
            var payments = await (
                from p in paymentsQuery
                join s in _context.FinancialConstants
                    on p.service_id equals s.service_id
                // Left Join على جدول employees عبر employee_id في سجل الدفعة
                join e in _context.Employees
                    on p.employee_id equals e.employee_id into empGroup
                from emp in empGroup.DefaultIfEmpty()
                orderby p.payment_date descending
                select new
                {
                    p.payment_id,
                    p.unit_id,
                    p.service_id,
                    p.total_service_fee,
                    p.payment_date,
                    p.payment_method,
                    p.accont_received,
                    p.employee_id,
                    ServicePrice = s.service_price,
                    ServiceName = s.service_name,
                    // الاسم الثلاثي من جدول employees
                    EmployeeFullName = emp != null
                        ? (emp.first_name ?? "") + " " +
                          (emp.second_name ?? "") + " " +
                          (emp.third_name ?? "")
                        : "غير محدد"
                }
            ).Take(100).ToListAsync();

            var result = payments
                .Select(p => new PaymentItemDto
                {
                    PaymentId = p.payment_id,
                    UnitId = p.unit_id,
                    ServiceId = p.service_id,
                    ServicePrice = p.ServicePrice,
                    Description = p.ServiceName,
                    TotalFee = $"{p.total_service_fee:N0} IQD",
                    ReceiptDate = p.payment_date.ToString("yyyy-MM-dd"),
                    PaymentMethod = p.payment_method ?? "كاش",
                    // accont_received محفوظة كـ decimal (0/1/2) في الموديل
                    AccountReceived = p.accont_received == 1 ? "الكهرباء"
                                    : p.accont_received == 2 ? "الانترنت"
                                    : "الادارة",
                    EmployeeName = p.EmployeeFullName.Trim(),
                    EmployeeId = (int)(p.employee_id ?? 0),
                    Status = GetStatus(p.payment_date),
                    StatusColor = GetStatusColor(p.payment_date)
                })
                .ToList();

            // فلترة الحالة بعد المعالجة (لأن GetStatus محلية)
            if (!string.IsNullOrWhiteSpace(status))
                result = result.Where(r => r.Status == status).ToList();

            return Ok(result);
        }

        // ─────────────────────────────────────────────
        // GET /api/Financials/payments/{unitId}
        // دفعات وحدة بعينها
        // ─────────────────────────────────────────────
        [HttpGet("payments/{unitId}")]
        public async Task<ActionResult<List<PaymentItemDto>>> GetUnitPayments(int unitId)
        {
            if (!await _context.HousingUnits.AnyAsync(u => u.unit_id == unitId))
                return NotFound(new { message = "الوحدة غير موجودة" });

            var payments = await (
                from p in _context.FinancialPayments
                where p.unit_id == unitId
                join s in _context.FinancialConstants
                    on p.service_id equals s.service_id
                join e in _context.Employees
                    on p.employee_id equals e.employee_id into empGroup
                from emp in empGroup.DefaultIfEmpty()
                orderby p.payment_date descending
                select new
                {
                    p.payment_id,
                    p.unit_id,
                    p.service_id,
                    p.total_service_fee,
                    p.payment_date,
                    p.payment_method,
                    p.accont_received,
                    p.employee_id,
                    ServicePrice = s.service_price,
                    ServiceName = s.service_name,
                    EmployeeFullName = emp != null
                        ? (emp.first_name ?? "") + " " +
                          (emp.second_name ?? "") + " " +
                          (emp.third_name ?? "")
                        : "غير محدد"
                }
            ).ToListAsync();

            var result = payments.Select(p => new PaymentItemDto
            {
                PaymentId = p.payment_id,
                UnitId = p.unit_id,
                ServiceId = p.service_id,
                ServicePrice = p.ServicePrice,
                Description = p.ServiceName,
                TotalFee = $"{p.total_service_fee:N0} IQD",
                ReceiptDate = p.payment_date.ToString("yyyy-MM-dd"),
                PaymentMethod = p.payment_method ?? "كاش",
                AccountReceived = p.accont_received == 1 ? "الكهرباء"
                                : p.accont_received == 2 ? "الانترنت"
                                : "الادارة",
                EmployeeName = p.EmployeeFullName.Trim(),
                EmployeeId = (int)(p.employee_id ?? 0),
                Status = GetStatus(p.payment_date),
                StatusColor = GetStatusColor(p.payment_date)
            }).ToList();

            return Ok(result);
        }

        // ─────────────────────────────────────────────
        // GET /api/Financials/invoices
        // فواتير الشهر الحالي لكل الوحدات المشغولة
        // ─────────────────────────────────────────────
        [HttpGet("invoices")]
        public async Task<ActionResult<List<InvoiceItemDto>>> GetInvoices()
        {
            var now = DateTime.Now;
            var currentMonth = now.Month;
            var currentYear = now.Year;

            var occupiedUnits = await _context.HousingUnits
                .Where(u => u.unit_status == "مشغول")
                .OrderBy(u => u.unit_id)
                .ToListAsync();

            var paidThisMonth = await _context.FinancialPayments
                .Where(p => p.payment_date.Month == currentMonth
                         && p.payment_date.Year == currentYear
                         && p.service_id == 5)
                .Select(p => p.unit_id)
                .ToHashSetAsync();

            var lastPayments = await _context.FinancialPayments
                .Where(p => p.service_id == 5)
                .GroupBy(p => p.unit_id)
                .Select(g => new
                {
                    UnitId = g.Key,
                    LastDate = g.Max(p => p.payment_date),
                    LastPayId = g.OrderByDescending(p => p.payment_date)
                                 .Select(p => p.payment_id).First()
                })
                .ToListAsync();

            var lastPayDict = lastPayments.ToDictionary(x => x.UnitId);

            var result = occupiedUnits.Select(u =>
            {
                bool paid = paidThisMonth.Contains(u.unit_id);
                lastPayDict.TryGetValue(u.unit_id, out var last);
                return new InvoiceItemDto
                {
                    UnitId = u.unit_id,
                    UnitType = u.unit_type ?? "شقة",
                    Status = paid ? "مدفوع" : "غير مدفوع",
                    StatusColor = paid ? "#28a745" : "#dc3545",
                    LastPayDate = last != null ? last.LastDate.ToString("yyyy-MM-dd") : "—",
                    LastPaymentId = last?.LastPayId ?? 0
                };
            }).ToList();

            return Ok(result);
        }

        // ─────────────────────────────────────────────
        // POST /api/Financials/register
        //
        // يستقبل employee_id من التطبيق (الموظف المسجّل دخوله)
        // ويتحقق من وجوده في جدول employees قبل الحفظ
        // ─────────────────────────────────────────────
        [HttpPost("register")]
        public async Task<ActionResult> RegisterPayment([FromBody] RegisterPaymentDto dto)
        {
            // التحقق من الوحدة في housing_units
            if (!await _context.HousingUnits.AnyAsync(u => u.unit_id == dto.UnitId))
                return BadRequest(new { message = "رقم الوحدة غير موجود في قاعدة البيانات" });

            // التحقق من الخدمة في financial_constants
            var service = await _context.FinancialConstants
                .FirstOrDefaultAsync(s => s.service_id == dto.ServiceId);
            if (service == null)
                return BadRequest(new { message = "نوع الخدمة غير موجود" });

            // التحقق من الموظف في employees
            if (dto.EmployeeId <= 0)
                return BadRequest(new { message = "لم يتم تحديد الموظف. يرجى تسجيل الدخول أولاً." });

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.employee_id == dto.EmployeeId);
            if (employee == null)
                return BadRequest(new
                {
                    message = $"الموظف رقم {dto.EmployeeId} غير موجود في قاعدة البيانات."
                });

            // تحويل accountReceived من نص إلى رقم (decimal في الموديل)
            decimal accountCode = dto.AccountReceived switch
            {
                "الكهرباء" => 1,
                "الانترنت" => 2,
                _ => 0
            };

            // إنشاء سجل الدفعة — employee_id = الموظف الفعلي المسجّل دخوله
            var payment = new FinancialPayment
            {
                unit_id = dto.UnitId,
                service_id = dto.ServiceId,
                total_service_fee = dto.Amount > 0 ? dto.Amount : service.service_price,
                employee_id = dto.EmployeeId,   // ← الموظف الحقيقي
                payment_method = dto.PaymentMethod,
                accont_received = accountCode,
                payment_date = dto.PaymentDate != default ? dto.PaymentDate : DateTime.Now
            };

            _context.FinancialPayments.Add(payment);
            await _context.SaveChangesAsync();

            string empFullName = string.Join(" ",
                new[] { employee.first_name, employee.second_name, employee.third_name }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim()));

            return Ok(new
            {
                message = "تم تسجيل الدفعة بنجاح",
                payment_id = payment.payment_id,
                employee_id = dto.EmployeeId,
                employee_name = empFullName
            });
        }

        // ─────────────────────────────────────────────
        // PUT /api/Financials/status/{paymentId}
        // ─────────────────────────────────────────────
        [HttpPut("status/{paymentId}")]
        public async Task<ActionResult> UpdatePaymentStatus(
            int paymentId, [FromBody] UpdateStatusDto dto)
        {
            var payment = await _context.FinancialPayments
                .FirstOrDefaultAsync(p => p.payment_id == paymentId);
            if (payment == null)
                return NotFound(new { message = "الدفعة غير موجودة" });

            payment.payment_date = dto.NewStatus switch
            {
                "مدفوع" => DateTime.Now,
                "مستحق" => DateTime.Now.AddDays(-60),
                "متأخر" => DateTime.Now.AddDays(-120),
                _ => payment.payment_date
            };

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "تم تحديث الحالة بنجاح",
                new_status = GetStatus(payment.payment_date),
                status_color = GetStatusColor(payment.payment_date)
            });
        }

        // ─────────────────────────────────────────────
        // دوال مساعدة لحساب الحالة بناءً على تاريخ الدفع
        // ─────────────────────────────────────────────
        private static string GetStatus(DateTime d)
        {
            var days = (DateTime.Now - d).TotalDays;
            return days <= 30 ? "مدفوع" : days <= 90 ? "مستحق" : "متأخر";
        }

        private static string GetStatusColor(DateTime d)
        {
            var days = (DateTime.Now - d).TotalDays;
            return days <= 30 ? "#28a745" : days <= 90 ? "#D4A017" : "#dc3545";
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────

    public class FinancialSummaryDto
    {
        public string TotalRevenue { get; set; } = "0 IQD";
        public string OutstandingAmount { get; set; } = "0 IQD";
        public string LateAmount { get; set; } = "0 IQD";
        public string MonthlyAvg { get; set; } = "0 IQD";
    }

    public class ServiceDto
    {
        public int ServiceId { get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public decimal ServicePrice { get; set; }
    }

    public class PaymentItemDto
    {
        public int PaymentId { get; set; }
        public int UnitId { get; set; }
        public int ServiceId { get; set; }
        public decimal ServicePrice { get; set; }
        public string Description { get; set; } = string.Empty;
        public string TotalFee { get; set; } = "0 IQD";
        public string ReceiptDate { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public string AccountReceived { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public int EmployeeId { get; set; }
        public string Status { get; set; } = "مدفوع";
        public string StatusColor { get; set; } = "#28a745";
    }

    /// <summary>
    /// يُستقبل من التطبيق عبر POST /register
    /// EmployeeId = employee_id من Preferences (الموظف المسجّل دخوله)
    /// </summary>
    public class RegisterPaymentDto
    {
        public int UnitId { get; set; }
        public int ServiceId { get; set; }
        public decimal Amount { get; set; }
        public int EmployeeId { get; set; }   // ← employee_id الفعلي
        public string PaymentMethod { get; set; } = "كاش";
        public string AccountReceived { get; set; } = "الادارة";
        public DateTime PaymentDate { get; set; }
    }

    public class UpdateStatusDto
    {
        public string NewStatus { get; set; } = string.Empty;
    }

    public class InvoiceItemDto
    {
        public int UnitId { get; set; }
        public string UnitType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusColor { get; set; } = "#888";
        public string LastPayDate { get; set; } = string.Empty;
        public int LastPaymentId { get; set; }
    }
}