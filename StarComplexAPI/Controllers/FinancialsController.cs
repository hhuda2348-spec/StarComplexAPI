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

            var occupiedUnitIds = await _context.HousingUnits
                .Where(u => u.unit_status == "مشغول")
                .Select(u => u.unit_id)
                .ToListAsync();

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

            decimal outstandingAmount =
                occupiedUnitIds.Except(paidThisMonth).Count() * monthlyRent;

            var threeMonthsAgo = now.AddMonths(-3);
            var paidLast3M = await _context.FinancialPayments
                .Where(p => p.payment_date >= threeMonthsAgo && p.service_id == 5)
                .Select(p => p.unit_id)
                .Distinct()
                .ToListAsync();

            decimal lateAmount =
                occupiedUnitIds.Except(paidLast3M).Count() * monthlyRent * 3;

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

            var payments = await (
                from p in paymentsQuery
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
                    AccountReceived = p.accont_received == 1 ? "الكهرباء"
                                    : p.accont_received == 2 ? "الانترنت"
                                    : "الادارة",
                    EmployeeName = p.EmployeeFullName.Trim(),
                    EmployeeId = (int)(p.employee_id ?? 0),
                    Status = GetStatus(p.payment_date),
                    StatusColor = GetStatusColor(p.payment_date)
                })
                .ToList();

            if (!string.IsNullOrWhiteSpace(status))
                result = result.Where(r => r.Status == status).ToList();

            return Ok(result);
        }

        // ─────────────────────────────────────────────
        // GET /api/Financials/payments/{unitId}
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
        // GET /api/Financials/overview
        // جدول موحّد: فواتير الإيجار الشهري + طلبات الصيانة المنفذة
        // كلٌّ منهما يُظهر حالة الدفع + معلومات الموظف إن وُجدت
        // ─────────────────────────────────────────────
        [HttpGet("overview")]
        public async Task<ActionResult<List<OverviewRowDto>>> GetOverview()
        {
            var now = DateTime.Now;
            var currentMonth = now.Month;
            var currentYear = now.Year;

            var rows = new List<OverviewRowDto>();

            // ── 1. فواتير الإيجار الشهري ───────────────────────────
            var occupiedUnits = await _context.HousingUnits
                .Where(u => u.unit_status == "مشغول")
                .OrderBy(u => u.unit_id)
                .ToListAsync();

            // آخر دفعة إيجار لكل وحدة في الشهر الحالي
            var rentPaymentsThisMonth = await (
                from p in _context.FinancialPayments
                where p.service_id == 5
                   && p.payment_date.Month == currentMonth
                   && p.payment_date.Year == currentYear
                join e in _context.Employees
                    on p.employee_id equals e.employee_id into empGroup
                from emp in empGroup.DefaultIfEmpty()
                select new
                {
                    p.unit_id,
                    p.payment_id,
                    p.total_service_fee,
                    p.payment_date,
                    EmployeeName = emp != null
                        ? (emp.first_name ?? "") + " " +
                          (emp.second_name ?? "") + " " +
                          (emp.third_name ?? "")
                        : "غير محدد"
                }
            ).ToListAsync();

            var rentPayDict = rentPaymentsThisMonth
                .GroupBy(p => p.unit_id)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.payment_date).First());

            decimal monthlyRent = await _context.FinancialConstants
                .Where(s => s.service_id == 5)
                .Select(s => s.service_price)
                .FirstOrDefaultAsync();

            foreach (var unit in occupiedUnits)
            {
                bool paid = rentPayDict.TryGetValue(unit.unit_id, out var pay);
                rows.Add(new OverviewRowDto
                {
                    RowType = "invoice",
                    RowIcon = "🧾",
                    UnitId = unit.unit_id,
                    ServiceId = 5,
                    ServiceName = "الإيجار الشهري",
                    Amount = paid ? $"{pay!.total_service_fee:N0} IQD" : $"{monthlyRent:N0} IQD",
                    AmountRaw = paid ? pay!.total_service_fee : monthlyRent,
                    PaymentId = paid ? pay!.payment_id : 0,
                    EmployeeName = paid ? pay!.EmployeeName.Trim() : "—",
                    DateLabel = paid ? pay!.payment_date.ToString("yyyy-MM-dd") : "—",
                    StatusLabel = paid ? "مدفوع" : "غير مدفوع",
                    StatusColor = paid ? "#28a745" : "#dc3545",
                    CanPay = !paid,
                    MaintenanceRequestId = 0
                });
            }

            // ── 2. طلبات الصيانة المنفذة ─────────────────────────
            var maintenanceDone = await (
                from req in _context.MaintenanceRequests
                where req.request_status == "تم تنفيذ الطلب"
                join svc in _context.FinancialConstants
                    on req.service_id equals svc.service_id
                select new
                {
                    req.request_id,
                    req.unit_id,
                    req.service_id,
                    req.request_date,
                    req.feedback,
                    SvcName = svc.service_name,
                    SvcPrice = svc.service_price
                }
            ).OrderByDescending(r => r.request_date).ToListAsync();

            // الدفعات المرتبطة بطلبات الصيانة (service_id != 5)
            var maintPayments = await (
                from p in _context.FinancialPayments
                where p.service_id != 5
                join e in _context.Employees
                    on p.employee_id equals e.employee_id into empGroup
                from emp in empGroup.DefaultIfEmpty()
                select new
                {
                    p.unit_id,
                    p.service_id,
                    p.payment_id,
                    p.total_service_fee,
                    p.payment_date,
                    EmployeeName = emp != null
                        ? (emp.first_name ?? "") + " " +
                          (emp.second_name ?? "") + " " +
                          (emp.third_name ?? "")
                        : "غير محدد"
                }
            ).ToListAsync();

            // نطابق طلب الصيانة مع أقرب دفعة من نفس الوحدة ونفس الخدمة
            foreach (var m in maintenanceDone)
            {
                var matchedPay = maintPayments
                    .Where(p => p.unit_id == m.unit_id
                             && p.service_id == m.service_id
                             && p.payment_date >= m.request_date)
                    .OrderBy(p => p.payment_date)
                    .FirstOrDefault();

                bool paid = matchedPay != null;

                rows.Add(new OverviewRowDto
                {
                    RowType = "maintenance",
                    RowIcon = "🔧",
                    UnitId = m.unit_id,
                    ServiceId = m.service_id,
                    ServiceName = m.SvcName,
                    Amount = paid
                        ? $"{matchedPay!.total_service_fee:N0} IQD"
                        : $"{m.SvcPrice:N0} IQD",
                    AmountRaw = paid ? matchedPay!.total_service_fee : m.SvcPrice,
                    PaymentId = paid ? matchedPay!.payment_id : 0,
                    EmployeeName = paid ? matchedPay!.EmployeeName.Trim() : "—",
                    DateLabel = m.request_date.ToString("yyyy-MM-dd"),
                    StatusLabel = paid ? "مدفوع" : "غير مدفوع",
                    StatusColor = paid ? "#28a745" : "#dc3545",
                    CanPay = !paid,
                    MaintenanceRequestId = m.request_id,
                    Feedback = m.feedback ?? string.Empty
                });
            }

            // ترتيب: رقم الوحدة تصاعدياً
            return Ok(rows.OrderBy(r => r.UnitId).ThenBy(r => r.RowType).ToList());
        }

        // ─────────────────────────────────────────────
        // POST /api/Financials/register
        // ─────────────────────────────────────────────
        [HttpPost("register")]
        public async Task<ActionResult> RegisterPayment([FromBody] RegisterPaymentDto dto)
        {
            if (!await _context.HousingUnits.AnyAsync(u => u.unit_id == dto.UnitId))
                return BadRequest(new { message = "رقم الوحدة غير موجود في قاعدة البيانات" });

            var service = await _context.FinancialConstants
                .FirstOrDefaultAsync(s => s.service_id == dto.ServiceId);
            if (service == null)
                return BadRequest(new { message = "نوع الخدمة غير موجود" });

            if (dto.EmployeeId <= 0)
                return BadRequest(new { message = "لم يتم تحديد الموظف. يرجى تسجيل الدخول أولاً." });

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.employee_id == dto.EmployeeId);
            if (employee == null)
                return BadRequest(new
                {
                    message = $"الموظف رقم {dto.EmployeeId} غير موجود في قاعدة البيانات."
                });

            decimal accountCode = dto.AccountReceived switch
            {
                "الكهرباء" => 1,
                "الانترنت" => 2,
                _ => 0
            };

            var payment = new FinancialPayment
            {
                unit_id = dto.UnitId,
                service_id = dto.ServiceId,
                total_service_fee = dto.Amount > 0 ? dto.Amount : service.service_price,
                employee_id = dto.EmployeeId,
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
        // POST /api/Financials/register-maintenance
        // تسجيل دفعة صيانة كاش مرتبطة بطلب صيانة محدد
        // ─────────────────────────────────────────────
        [HttpPost("register-maintenance")]
        public async Task<ActionResult> RegisterMaintenancePayment(
            [FromBody] RegisterMaintenancePaymentDto dto)
        {
            // التحقق من الوحدة
            if (!await _context.HousingUnits.AnyAsync(u => u.unit_id == dto.UnitId))
                return BadRequest(new { message = "رقم الوحدة غير موجود" });

            // التحقق من طلب الصيانة
            var request = await _context.MaintenanceRequests
                .FirstOrDefaultAsync(r => r.request_id == dto.MaintenanceRequestId
                                       && r.unit_id == dto.UnitId);
            if (request == null)
                return BadRequest(new { message = "طلب الصيانة غير موجود أو لا ينتمي لهذه الوحدة" });

            if (request.request_status != "تم تنفيذ الطلب")
                return BadRequest(new { message = "لا يمكن تسجيل دفعة لطلب لم يتم تنفيذه بعد" });

            // التحقق من الخدمة
            var service = await _context.FinancialConstants
                .FirstOrDefaultAsync(s => s.service_id == request.service_id);
            if (service == null)
                return BadRequest(new { message = "نوع الخدمة غير موجود" });

            // التحقق من الموظف
            if (dto.EmployeeId <= 0)
                return BadRequest(new { message = "لم يتم تحديد الموظف. يرجى تسجيل الدخول أولاً." });

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.employee_id == dto.EmployeeId);
            if (employee == null)
                return BadRequest(new { message = $"الموظف رقم {dto.EmployeeId} غير موجود." });

            // التحقق من عدم وجود دفعة سابقة لنفس الطلب
            bool alreadyPaid = await _context.FinancialPayments
                .AnyAsync(p => p.unit_id == dto.UnitId
                            && p.service_id == request.service_id
                            && p.payment_date >= request.request_date);
            if (alreadyPaid)
                return BadRequest(new { message = "تم تسجيل دفعة لهذا الطلب مسبقاً" });

            var payment = new FinancialPayment
            {
                unit_id = dto.UnitId,
                service_id = request.service_id,
                total_service_fee = dto.Amount > 0 ? dto.Amount : service.service_price,
                employee_id = dto.EmployeeId,
                payment_method = "كاش",
                accont_received = 0, // الادارة دائماً لدفعات الصيانة
                payment_date = DateTime.Now
            };

            _context.FinancialPayments.Add(payment);
            await _context.SaveChangesAsync();

            string empFullName = string.Join(" ",
                new[] { employee.first_name, employee.second_name, employee.third_name }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim()));

            return Ok(new
            {
                message = "تم تسجيل دفعة الصيانة بنجاح",
                payment_id = payment.payment_id,
                employee_name = empFullName,
                amount = payment.total_service_fee
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
        // دوال مساعدة
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
    /// صف موحّد للجدول الإجمالي (فاتورة إيجار أو صيانة منفذة)
    /// </summary>
    public class OverviewRowDto
    {
        public string RowType { get; set; } = string.Empty;       // "invoice" | "maintenance"
        public string RowIcon { get; set; } = string.Empty;
        public int UnitId { get; set; }
        public int ServiceId { get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public string Amount { get; set; } = "0 IQD";
        public decimal AmountRaw { get; set; }
        public int PaymentId { get; set; }                          // 0 = غير مدفوع
        public string EmployeeName { get; set; } = "—";
        public string DateLabel { get; set; } = "—";
        public string StatusLabel { get; set; } = string.Empty;
        public string StatusColor { get; set; } = "#888";
        public bool CanPay { get; set; }                            // true = يمكن تسجيل دفعة
        public int MaintenanceRequestId { get; set; }               // 0 للإيجار
        public string Feedback { get; set; } = string.Empty;
    }

    public class RegisterPaymentDto
    {
        public int UnitId { get; set; }
        public int ServiceId { get; set; }
        public decimal Amount { get; set; }
        public int EmployeeId { get; set; }
        public string PaymentMethod { get; set; } = "كاش";
        public string AccountReceived { get; set; } = "الادارة";
        public DateTime PaymentDate { get; set; }
    }

    /// <summary>
    /// يُستخدم لتسجيل دفعة صيانة كاش من الشؤون المالية
    /// </summary>
    public class RegisterMaintenancePaymentDto
    {
        public int UnitId { get; set; }
        public int MaintenanceRequestId { get; set; }
        public decimal Amount { get; set; }
        public int EmployeeId { get; set; }
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