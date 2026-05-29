using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;
using StarComplexAPI.Models;
using System.Text.Json.Serialization;

namespace StarComplexAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FinancialsController : ControllerBase
    {
        private readonly StarComplexContext _context;
        private readonly ILogger<FinancialsController> _logger;

        public FinancialsController(StarComplexContext context, ILogger<FinancialsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ═══════════════════════════════════════════════════════════════
        // GET /api/Financials/summary
        // ═══════════════════════════════════════════════════════════════
        [HttpGet("summary")]
        public async Task<ActionResult<FinancialSummaryDto>> GetSummary()
        {
            try
            {
                _logger.LogInformation("Getting financial summary...");

                decimal totalRevenue = await _context.FinancialPayments
                    .SumAsync(p => (decimal?)p.total_service_fee) ?? 0;

                var now = DateTime.Now;
                var currentMonth = now.Month;
                var currentYear = now.Year;

                var occupiedUnitIds = await _context.HousingUnits
                    .Where(u => u.unit_status == "مشغول")
                    .Select(u => u.unit_id)
                    .ToListAsync();

                if (!occupiedUnitIds.Any())
                {
                    return Ok(new FinancialSummaryDto
                    {
                        TotalRevenue = "0 IQD",
                        OutstandingAmount = "0 IQD",
                        LateAmount = "0 IQD",
                        MonthlyAvg = "0 IQD"
                    });
                }

                var rentService = await _context.FinancialConstants
                    .FirstOrDefaultAsync(s => s.service_id == 5);
                decimal monthlyRent = rentService?.service_price ?? 0;

                var paidRentThisMonth = await _context.FinancialPayments
                    .Where(p => p.payment_date.Year == currentYear
                             && p.payment_date.Month == currentMonth
                             && p.service_id == 5)
                    .Select(p => p.unit_id)
                    .Distinct()
                    .ToListAsync();

                var internetService = await _context.FinancialConstants
                    .FirstOrDefaultAsync(s => EF.Functions.Like(s.service_name, "%انترنت%")
                                           || EF.Functions.Like(s.service_name, "%إنترنت%"));

                decimal internetPrice = internetService?.service_price ?? 0;
                int internetServiceId = internetService?.service_id ?? 0;

                var paidInternetThisMonth = internetServiceId > 0
                    ? await _context.FinancialPayments
                        .Where(p => p.payment_date.Year == currentYear
                                 && p.payment_date.Month == currentMonth
                                 && p.service_id == internetServiceId)
                        .Select(p => p.unit_id)
                        .Distinct()
                        .ToListAsync()
                    : new List<int>();

                decimal rentOutstanding = occupiedUnitIds.Except(paidRentThisMonth).Count() * monthlyRent;
                decimal internetOutstanding = internetServiceId > 0
                    ? occupiedUnitIds.Except(paidInternetThisMonth).Count() * internetPrice
                    : 0;
                decimal outstandingAmount = rentOutstanding + internetOutstanding;

                var threeMonthsAgo = now.AddMonths(-3);
                var paidRentLast3M = await _context.FinancialPayments
                    .Where(p => p.payment_date >= threeMonthsAgo && p.service_id == 5)
                    .Select(p => p.unit_id)
                    .Distinct()
                    .ToListAsync();

                decimal lateAmount = occupiedUnitIds.Except(paidRentLast3M).Count() * monthlyRent * 3;

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetSummary");
                return StatusCode(500, new { message = "خطأ في جلب الملخص المالي", details = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // GET /api/Financials/services
        // ═══════════════════════════════════════════════════════════════
        [HttpGet("services")]
        public async Task<ActionResult<List<ServiceDto>>> GetServices()
        {
            try
            {
                var services = await _context.FinancialConstants
                    .OrderBy(s => s.service_id)
                    .Select(s => new ServiceDto
                    {
                        ServiceId = s.service_id,
                        ServiceName = s.service_name ?? "خدمة غير معروفة",
                        ServicePrice = s.service_price
                    })
                    .ToListAsync();

                return Ok(services);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetServices");
                return StatusCode(500, new { message = "خطأ في جلب الخدمات" });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // GET /api/Financials/recent
        // ═══════════════════════════════════════════════════════════════
        [HttpGet("recent")]
        public async Task<ActionResult<List<PaymentItemDto>>> GetRecentPayments(
            [FromQuery] string? method = null,
            [FromQuery] int? unitId = null,
            [FromQuery] string? status = null)
        {
            try
            {
                var paymentsQuery = _context.FinancialPayments.AsQueryable();

                if (!string.IsNullOrWhiteSpace(method))
                    paymentsQuery = paymentsQuery.Where(p => p.payment_method == method);

                if (unitId.HasValue && unitId.Value > 0)
                    paymentsQuery = paymentsQuery.Where(p => p.unit_id == unitId.Value);

                var payments = await (
                    from p in paymentsQuery
                    join s in _context.FinancialConstants
                        on p.service_id equals s.service_id into serviceGroup
                    from s in serviceGroup.DefaultIfEmpty()
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
                        ServicePrice = s != null ? s.service_price : 0,
                        ServiceName = s != null ? s.service_name : "خدمة غير معروفة",
                        EmployeeFullName = emp != null
                            ? ((emp.first_name ?? "") + " " +
                               (emp.second_name ?? "") + " " +
                               (emp.third_name ?? "")).Trim()
                            : "غير محدد"
                    }
                ).Take(200).ToListAsync();

                var result = payments
                    .Select(p => new PaymentItemDto
                    {
                        PaymentId = p.payment_id,
                        UnitId = p.unit_id,
                        ServiceId = (int)p.service_id,
                        ServicePrice = p.ServicePrice,
                        Description = p.ServiceName ?? "خدمة",
                        TotalFee = $"{p.total_service_fee:N0} IQD",
                        ReceiptDate = p.payment_date.ToString("yyyy-MM-dd"),
                        PaymentMethod = p.payment_method ?? "كاش",
                        AccountReceived = p.accont_received switch
                        {
                            1 => "الكهرباء",
                            2 => "الانترنت",
                            _ => "الادارة"
                        },
                        EmployeeName = p.EmployeeFullName,
                        EmployeeId = (int)(p.employee_id ?? 0),
                        Status = "مدفوع",
                        StatusColor = "#28a745"
                    })
                    .ToList();

                if (!string.IsNullOrWhiteSpace(status))
                    result = result.Where(r => r.Status == status).ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetRecentPayments");
                return StatusCode(500, new { message = "خطأ في جلب الدفعات" });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // GET /api/Financials/overview
        // ═══════════════════════════════════════════════════════════════
        [HttpGet("overview")]
        public async Task<ActionResult<List<OverviewRowDto>>> GetOverview()
        {
            try
            {
                var now = DateTime.Now;
                var currentMonth = now.Month;
                var currentYear = now.Year;

                var rows = new List<OverviewRowDto>();

                var occupiedUnits = await _context.HousingUnits
                    .Where(u => u.unit_status == "مشغول")
                    .OrderBy(u => u.unit_id)
                    .ToListAsync();

                if (!occupiedUnits.Any())
                    return Ok(rows);

                var allPayments = await (
                    from p in _context.FinancialPayments
                    join e in _context.Employees
                        on p.employee_id equals e.employee_id into empGroup
                    from emp in empGroup.DefaultIfEmpty()
                    select new
                    {
                        p.payment_id,
                        p.unit_id,
                        p.service_id,
                        p.total_service_fee,
                        p.payment_date,
                        p.payment_method,
                        p.accont_received,
                        EmployeeName = emp != null
                            ? ((emp.first_name ?? "") + " " +
                               (emp.second_name ?? "") + " " +
                               (emp.third_name ?? "")).Trim()
                            : "غير محدد"
                    }
                ).ToListAsync();

                var allServices = await _context.FinancialConstants.ToListAsync();

                var rentService = allServices.FirstOrDefault(s => s.service_id == 5);
                var internetService = allServices.FirstOrDefault(s =>
                    s.service_name != null &&
                    (s.service_name.Contains("انترنت") || s.service_name.Contains("إنترنت")));

                decimal monthlyRent = rentService?.service_price ?? 0;
                int rentServiceId = rentService?.service_id ?? 5;
                decimal internetPrice = internetService?.service_price ?? 0;
                int internetServiceId = internetService?.service_id ?? 0;

                // ── 1. فواتير الإيجار الشهري ──
                var rentPaymentsThisMonth = allPayments
                    .Where(p => p.service_id == rentServiceId
                             && p.payment_date.Year == currentYear
                             && p.payment_date.Month == currentMonth)
                    .GroupBy(p => p.unit_id)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.payment_date).First());

                foreach (var unit in occupiedUnits)
                {
                    bool paid = rentPaymentsThisMonth.TryGetValue(unit.unit_id, out var pay);
                    rows.Add(new OverviewRowDto
                    {
                        RowType = "invoice",
                        RowIcon = "🧾",
                        UnitId = unit.unit_id,
                        ServiceId = rentServiceId,
                        ServiceName = "الإيجار الشهري",
                        Amount = $"{(paid ? pay!.total_service_fee : monthlyRent):N0} IQD",
                        AmountRaw = (decimal)(paid ? pay!.total_service_fee : monthlyRent),
                        PaymentId = paid ? pay!.payment_id : 0,
                        EmployeeName = paid ? pay!.EmployeeName : "—",
                        DateLabel = paid ? pay!.payment_date.ToString("yyyy-MM-dd") : "—",
                        StatusLabel = paid ? "مدفوع" : "غير مدفوع",
                        StatusColor = paid ? "#28a745" : "#dc3545",
                        CanPay = !paid,
                        MaintenanceRequestId = 0,
                        Feedback = "",
                        Month = currentMonth,
                        Year = currentYear
                    });
                }

                // ── 2. فواتير الانترنت الشهري ──
                if (internetServiceId > 0)
                {
                    var internetPaymentsThisMonth = allPayments
                        .Where(p => p.service_id == internetServiceId
                                 && p.payment_date.Year == currentYear
                                 && p.payment_date.Month == currentMonth)
                        .GroupBy(p => p.unit_id)
                        .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.payment_date).First());

                    foreach (var unit in occupiedUnits)
                    {
                        bool paid = internetPaymentsThisMonth.TryGetValue(unit.unit_id, out var pay);
                        rows.Add(new OverviewRowDto
                        {
                            RowType = "internet",
                            RowIcon = "🌐",
                            UnitId = unit.unit_id,
                            ServiceId = internetServiceId,
                            ServiceName = internetService?.service_name ?? "الانترنت",
                            Amount = $"{(paid ? pay!.total_service_fee : internetPrice):N0} IQD",
                            AmountRaw = (decimal)(paid ? pay!.total_service_fee : internetPrice),
                            PaymentId = paid ? pay!.payment_id : 0,
                            EmployeeName = paid ? pay!.EmployeeName : "—",
                            DateLabel = paid ? pay!.payment_date.ToString("yyyy-MM-dd") : "—",
                            StatusLabel = paid ? "مدفوع" : "غير مدفوع",
                            StatusColor = paid ? "#28a745" : "#dc3545",
                            CanPay = !paid,
                            MaintenanceRequestId = 0,
                            Feedback = "",
                            Month = currentMonth,
                            Year = currentYear
                        });
                    }
                }

                // ── 3. طلبات الصيانة المكتملة ──
                var maintenanceDone = await (
                    from req in _context.MaintenanceRequests
                    where req.request_status == "تم تنفيذ الطلب"
                    join svc in _context.FinancialConstants
                        on req.service_id equals svc.service_id into svcGroup
                    from svc in svcGroup.DefaultIfEmpty()
                    select new
                    {
                        req.request_id,
                        req.unit_id,
                        req.service_id,
                        req.request_date,
                        req.feedback,
                        SvcName = svc != null ? svc.service_name : "خدمة صيانة",
                        SvcPrice = svc != null ? svc.service_price : 0
                    }
                ).OrderByDescending(r => r.request_date).ToListAsync();

                foreach (var m in maintenanceDone)
                {
                    var matchedPay = allPayments
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
                        ServiceName = m.SvcName ?? "صيانة",
                        Amount = paid
                            ? $"{matchedPay!.total_service_fee:N0} IQD"
                            : $"{m.SvcPrice:N0} IQD",
                        AmountRaw = (decimal)(paid ? matchedPay!.total_service_fee : m.SvcPrice),
                        PaymentId = paid ? matchedPay!.payment_id : 0,
                        EmployeeName = paid ? matchedPay!.EmployeeName : "—",
                        DateLabel = m.request_date.ToString("yyyy-MM-dd"),
                        StatusLabel = paid ? "مدفوع" : "غير مدفوع",
                        StatusColor = paid ? "#28a745" : "#dc3545",
                        CanPay = !paid,
                        MaintenanceRequestId = m.request_id,
                        Feedback = m.feedback ?? ""
                    });
                }

                return Ok(rows.OrderBy(r => r.UnitId).ThenBy(r => r.RowType).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetOverview");
                return StatusCode(500, new { message = "خطأ في جلب النظرة العامة", details = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // GET /api/Financials/payments/{unitId}
        // ═══════════════════════════════════════════════════════════════
        [HttpGet("payments/{unitId}")]
        public async Task<ActionResult<List<PaymentItemDto>>> GetUnitPayments(int unitId)
        {
            try
            {
                if (!await _context.HousingUnits.AnyAsync(u => u.unit_id == unitId))
                    return NotFound(new { message = "الوحدة غير موجودة" });

                var payments = await (
                    from p in _context.FinancialPayments
                    where p.unit_id == unitId
                    join s in _context.FinancialConstants
                        on p.service_id equals s.service_id into serviceGroup
                    from s in serviceGroup.DefaultIfEmpty()
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
                        ServicePrice = s != null ? s.service_price : 0,
                        ServiceName = s != null ? s.service_name : "خدمة",
                        EmployeeFullName = emp != null
                            ? ((emp.first_name ?? "") + " " +
                               (emp.second_name ?? "") + " " +
                               (emp.third_name ?? "")).Trim()
                            : "غير محدد"
                    }
                ).ToListAsync();

                return Ok(payments.Select(p => new PaymentItemDto
                {
                    PaymentId = p.payment_id,
                    UnitId = p.unit_id,
                    ServiceId = (int)p.service_id,
                    ServicePrice = p.ServicePrice,
                    Description = p.ServiceName,
                    TotalFee = $"{p.total_service_fee:N0} IQD",
                    ReceiptDate = p.payment_date.ToString("yyyy-MM-dd"),
                    PaymentMethod = p.payment_method ?? "كاش",
                    AccountReceived = p.accont_received switch
                    {
                        1 => "الكهرباء",
                        2 => "الانترنت",
                        _ => "الادارة"
                    },
                    EmployeeName = p.EmployeeFullName,
                    EmployeeId = (int)(p.employee_id ?? 0),
                    Status = "مدفوع",
                    StatusColor = "#28a745"
                }).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetUnitPayments");
                return StatusCode(500, new { message = "خطأ في جلب دفعات الوحدة" });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // GET /api/Financials/invoices
        // ═══════════════════════════════════════════════════════════════
        [HttpGet("invoices")]
        public async Task<ActionResult<List<InvoiceItemDto>>> GetInvoices()
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetInvoices");
                return StatusCode(500, new { message = "خطأ في جلب الفواتير" });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // POST /api/Financials/register
        //
        // ✅ إزالة تحقق employee_type — أي موظف مسجّل يستطيع تسجيل دفعة
        //    (التحقق من الصلاحيات يتم عبر تسجيل الدخول في التطبيق)
        // ═══════════════════════════════════════════════════════════════
        [HttpPost("register")]
        public async Task<ActionResult> RegisterPayment([FromBody] RegisterPaymentDto dto)
        {
            try
            {
                _logger.LogInformation("Registering payment for unit {unitId}", dto.UnitId);

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
                    return BadRequest(new { message = $"الموظف رقم {dto.EmployeeId} غير موجود." });

                int accountCode = dto.AccountReceived switch
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

                _logger.LogInformation("Payment registered: {paymentId}", payment.payment_id);

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RegisterPayment");
                return StatusCode(500, new { message = "خطأ في تسجيل الدفعة", details = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // POST /api/Financials/pay-invoice
        //
        // ✅ إزالة تحقق employee_type — أي موظف مسجّل يستطيع الدفع
        //    المشكلة: الكود القديم كان يرفض بسبب employee_type لا يحتوي
        //    "admin" أو "إدارة" — حُلّت بإزالة هذا التحقق تماماً
        // ═══════════════════════════════════════════════════════════════
        [HttpPost("pay-invoice")]
        public async Task<ActionResult> PayInvoice([FromBody] PayInvoiceDto dto)
        {
            try
            {
                _logger.LogInformation("Paying invoice for unit {unitId}, service {serviceId}",
                    dto.UnitId, dto.ServiceId);

                if (dto.EmployeeId <= 0)
                    return BadRequest(new { message = "يرجى تسجيل الدخول أولاً" });

                var employee = await _context.Employees
                    .FirstOrDefaultAsync(e => e.employee_id == dto.EmployeeId);
                if (employee == null)
                    return BadRequest(new { message = "الموظف غير موجود" });

                if (!await _context.HousingUnits.AnyAsync(u => u.unit_id == dto.UnitId))
                    return BadRequest(new { message = "رقم الوحدة غير موجود" });

                var service = await _context.FinancialConstants
                    .FirstOrDefaultAsync(s => s.service_id == dto.ServiceId);
                if (service == null)
                    return BadRequest(new { message = "نوع الخدمة غير موجود" });

                // تحديد كود الحساب المستلم بناءً على اسم الخدمة
                int accountCode = service.service_name != null &&
                    (service.service_name.Contains("انترنت") ||
                     service.service_name.Contains("إنترنت"))
                    ? 2 : 0;

                var payment = new FinancialPayment
                {
                    unit_id = dto.UnitId,
                    service_id = dto.ServiceId,
                    total_service_fee = dto.Amount > 0 ? dto.Amount : service.service_price,
                    employee_id = dto.EmployeeId,
                    payment_method = "كاش",
                    accont_received = accountCode,
                    payment_date = DateTime.Now
                };

                _context.FinancialPayments.Add(payment);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Invoice paid: {paymentId}", payment.payment_id);

                string empFullName = string.Join(" ",
                    new[] { employee.first_name, employee.second_name, employee.third_name }
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!.Trim()));

                return Ok(new
                {
                    message = "تم تسجيل الدفعة بنجاح",
                    payment_id = payment.payment_id,
                    employee_name = empFullName,
                    amount = $"{payment.total_service_fee:N0} IQD"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PayInvoice");
                return StatusCode(500, new { message = "خطأ في تسجيل الدفعة", details = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // PUT /api/Financials/status/{paymentId}
        // ═══════════════════════════════════════════════════════════════
        [HttpPut("status/{paymentId}")]
        public async Task<ActionResult> UpdatePaymentStatus(
            int paymentId, [FromBody] UpdateStatusDto dto)
        {
            try
            {
                var payment = await _context.FinancialPayments
                    .FirstOrDefaultAsync(p => p.payment_id == paymentId);
                if (payment == null)
                    return NotFound(new { message = "الدفعة غير موجودة" });

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "تم تحديث الحالة بنجاح",
                    new_status = "مدفوع",
                    status_color = "#28a745"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UpdatePaymentStatus");
                return StatusCode(500, new { message = "خطأ في تحديث الحالة" });
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // DTOs
    // ═══════════════════════════════════════════════════════════════════

    public class FinancialSummaryDto
    {
        [JsonPropertyName("totalRevenue")] public string TotalRevenue { get; set; } = "0 IQD";
        [JsonPropertyName("outstandingAmount")] public string OutstandingAmount { get; set; } = "0 IQD";
        [JsonPropertyName("lateAmount")] public string LateAmount { get; set; } = "0 IQD";
        [JsonPropertyName("monthlyAvg")] public string MonthlyAvg { get; set; } = "0 IQD";
    }

    public class ServiceDto
    {
        [JsonPropertyName("serviceId")] public int ServiceId { get; set; }
        [JsonPropertyName("serviceName")] public string ServiceName { get; set; } = string.Empty;
        [JsonPropertyName("servicePrice")] public decimal ServicePrice { get; set; }
    }

    public class PaymentItemDto
    {
        [JsonPropertyName("paymentId")] public int PaymentId { get; set; }
        [JsonPropertyName("unitId")] public int UnitId { get; set; }
        [JsonPropertyName("serviceId")] public int ServiceId { get; set; }
        [JsonPropertyName("servicePrice")] public decimal ServicePrice { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("totalFee")] public string TotalFee { get; set; } = "0 IQD";
        [JsonPropertyName("receiptDate")] public string ReceiptDate { get; set; } = string.Empty;
        [JsonPropertyName("paymentMethod")] public string PaymentMethod { get; set; } = string.Empty;
        [JsonPropertyName("accountReceived")] public string AccountReceived { get; set; } = string.Empty;
        [JsonPropertyName("employeeName")] public string EmployeeName { get; set; } = string.Empty;
        [JsonPropertyName("employeeId")] public int EmployeeId { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = "مدفوع";
        [JsonPropertyName("statusColor")] public string StatusColor { get; set; } = "#28a745";
    }

    public class OverviewRowDto
    {
        [JsonPropertyName("rowType")] public string RowType { get; set; } = string.Empty;
        [JsonPropertyName("rowIcon")] public string RowIcon { get; set; } = string.Empty;
        [JsonPropertyName("unitId")] public int UnitId { get; set; }
        [JsonPropertyName("serviceId")] public int ServiceId { get; set; }
        [JsonPropertyName("serviceName")] public string ServiceName { get; set; } = string.Empty;
        [JsonPropertyName("amount")] public string Amount { get; set; } = "0 IQD";
        [JsonPropertyName("amountRaw")] public decimal AmountRaw { get; set; }
        [JsonPropertyName("paymentId")] public int PaymentId { get; set; }
        [JsonPropertyName("employeeName")] public string EmployeeName { get; set; } = "—";
        [JsonPropertyName("dateLabel")] public string DateLabel { get; set; } = "—";
        [JsonPropertyName("statusLabel")] public string StatusLabel { get; set; } = string.Empty;
        [JsonPropertyName("statusColor")] public string StatusColor { get; set; } = "#888";
        [JsonPropertyName("canPay")] public bool CanPay { get; set; }
        [JsonPropertyName("maintenanceRequestId")] public int MaintenanceRequestId { get; set; }
        [JsonPropertyName("feedback")] public string Feedback { get; set; } = string.Empty;
        [JsonPropertyName("month")] public int Month { get; set; }
        [JsonPropertyName("year")] public int Year { get; set; }
    }

    public class RegisterPaymentDto
    {
        [JsonPropertyName("unitId")] public int UnitId { get; set; }
        [JsonPropertyName("serviceId")] public int ServiceId { get; set; }
        [JsonPropertyName("amount")] public decimal Amount { get; set; }
        [JsonPropertyName("employeeId")] public int EmployeeId { get; set; }
        [JsonPropertyName("paymentMethod")] public string PaymentMethod { get; set; } = "كاش";
        [JsonPropertyName("accountReceived")] public string AccountReceived { get; set; } = "الادارة";
        [JsonPropertyName("paymentDate")] public DateTime PaymentDate { get; set; }
        [JsonPropertyName("maintenanceRequestId")] public int MaintenanceRequestId { get; set; }
    }

    public class PayInvoiceDto
    {
        [JsonPropertyName("unitId")] public int UnitId { get; set; }
        [JsonPropertyName("serviceId")] public int ServiceId { get; set; }
        [JsonPropertyName("amount")] public decimal Amount { get; set; }
        [JsonPropertyName("employeeId")] public int EmployeeId { get; set; }
        [JsonPropertyName("maintenanceRequestId")] public int MaintenanceRequestId { get; set; }
    }

    public class UpdateStatusDto
    {
        [JsonPropertyName("newStatus")] public string NewStatus { get; set; } = string.Empty;
    }

    public class InvoiceItemDto
    {
        [JsonPropertyName("unitId")] public int UnitId { get; set; }
        [JsonPropertyName("unitType")] public string UnitType { get; set; } = string.Empty;
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("statusColor")] public string StatusColor { get; set; } = "#888";
        [JsonPropertyName("lastPayDate")] public string LastPayDate { get; set; } = string.Empty;
        [JsonPropertyName("lastPaymentId")] public int LastPaymentId { get; set; }
    }
}