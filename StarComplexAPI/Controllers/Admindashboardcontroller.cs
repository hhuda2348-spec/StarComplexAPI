using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;
using StarComplexAPI.Models;

namespace StarComplexAPI.Controllers
{
    // ══════════════════════════════════════════════════════════════
    //  Response DTOs
    // ══════════════════════════════════════════════════════════════
    public class DashboardKpiDto
    {
        public int TotalResidents { get; set; }
        public string ResidentsSub { get; set; } = string.Empty;
        public int OccupiedUnits { get; set; }
        public int TotalUnits { get; set; }
        public string OccupancyRate { get; set; } = string.Empty;
        public int PendingVisits { get; set; }
        public string VisitsSub { get; set; } = string.Empty;
        public int OpenMaintenance { get; set; }
        public string MaintenanceSub { get; set; } = string.Empty;
        public decimal TotalRevenueRaw { get; set; }
        public string TotalRevenue { get; set; } = string.Empty;
        public decimal MonthlyRevenueRaw { get; set; }
        public string MonthlyRevenue { get; set; } = string.Empty;
        public string CurrentMonthLabel { get; set; } = string.Empty;
        public string TopService { get; set; } = string.Empty;
        public string TopServiceCount { get; set; } = string.Empty;
        public string RecentPaymentsSub { get; set; } = string.Empty;
        public string PaymentsCountLabel { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeTitle { get; set; } = string.Empty;
    }

    public class DashboardPaymentItemDto
    {
        public string PaymentId { get; set; } = string.Empty;
        public string UnitId { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public decimal TotalFeeRaw { get; set; }
        public string TotalFee { get; set; } = string.Empty;
        public DateTime PaymentDateRaw { get; set; }
        public string PaymentDate { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
    }

    public class DashboardMaintenanceItemDto
    {
        public string RequestId { get; set; } = string.Empty;
        public string UnitId { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public DateTime RequestDateRaw { get; set; }
        public string RequestDate { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusColor { get; set; } = string.Empty;
    }

    public class DashboardResponseDto
    {
        public DashboardKpiDto Kpi { get; set; } = new();
        public List<DashboardPaymentItemDto> RecentPayments { get; set; } = new();
        public List<DashboardMaintenanceItemDto> RecentMaintenance { get; set; } = new();
    }

    // ══════════════════════════════════════════════════════════════
    //  Request DTOs
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// إضافة للقائمة السوداء — PersonName*, EmployeeId*, Reason (اختياري)
    /// </summary>
    public class AddBlacklistRequest
    {
        public string PersonName { get; set; } = string.Empty;
        public int EmployeeId { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>
    /// إضافة موظف — EmployeeCode*, FirstName*, وباقي الحقول اختيارية
    /// </summary>
    public class AddEmployeeRequest
    {
        public string EmployeeCode { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string? SecondName { get; set; }
        public string? ThirdName { get; set; }
        public string? JobTitle { get; set; }
        public string? PhoneNumber { get; set; }
        public string? NationalIdFrontPath { get; set; }
        public string? NationalIdBackPath { get; set; }
    }

    public class AddResidentRequest
    {
        public int UnitId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string? SecondName { get; set; }
        public string? ThirdName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? ResidentType { get; set; }
        public int? FamilyMembersCount { get; set; }
    }

    public class ApproveVisitRequest
    {
        public int VisitId { get; set; }
        public string NewStatus { get; set; } = "مقبولة";
        public DateTime? ExpiryDate { get; set; }
    }

    public class CreateMaintenanceRequest
    {
        public int UnitId { get; set; }
        public int ServiceId { get; set; }
        public string? RequestStatus { get; set; } = "مفتوح";
        public string? Feedback { get; set; }
    }

    // ══════════════════════════════════════════════════════════════
    //  Controller
    // ══════════════════════════════════════════════════════════════
    [ApiController]
    [Route("api/[controller]")]
    public class AdminDashboardController : ControllerBase
    {
        private readonly StarComplexContext _db;

        public AdminDashboardController(StarComplexContext db) => _db = db;

        // ── Dashboard كامل ──
        [HttpGet("full/{employeeId}")]
        public async Task<ActionResult<DashboardResponseDto>> GetFullDashboard(int employeeId)
        {
            var employee = await _db.Employees.FindAsync(employeeId);
            var employeeName = employee is null
                ? "غير معروف"
                : string.Join(" ", new[] { employee.first_name, employee.second_name, employee.third_name }
                    .Where(n => !string.IsNullOrWhiteSpace(n)));
            var employeeTitle = employee?.job_title ?? string.Empty;

            var totalResidents = await _db.Residents.CountAsync();
            var residentsSub = $"مع {await _db.FamilyMembers.CountAsync()} فرد عائلة";
            var occupiedUnits = await _db.HousingUnits.CountAsync(u => u.unit_status == "مشغول");
            var totalUnits = await _db.HousingUnits.CountAsync();
            var occupancyPct = totalUnits > 0 ? (int)Math.Round((double)occupiedUnits / totalUnits * 100) : 0;
            var pendingVisits = await _db.Visits.CountAsync(v => v.visit_status == "معلقة");
            var todayVisits = await _db.Visits.CountAsync(v => v.visit_date.HasValue && v.visit_date.Value.Date == DateTime.Today);
            var openMaintenance = await _db.MaintenanceRequests.CountAsync(m => m.request_status == "مفتوح");
            var inProgress = await _db.MaintenanceRequests.CountAsync(m => m.request_status == "قيد التنفيذ");

            var totalRevenueRaw = (decimal)await _db.FinancialPayments.SumAsync(p => p.total_service_fee);
            var now = DateTime.Now;
            var monthlyRevenueRaw = (decimal)await _db.FinancialPayments
                .Where(p => p.payment_date.Year == now.Year && p.payment_date.Month == now.Month)
                .SumAsync(p => p.total_service_fee);

            var arabicMonths = new[] {
                "يناير","فبراير","مارس","أبريل","مايو","يونيو",
                "يوليو","أغسطس","سبتمبر","أكتوبر","نوفمبر","ديسمبر"
            };

            var topServiceData = await _db.FinancialPayments
                .GroupBy(p => p.service_id)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .FirstOrDefaultAsync();

            string topServiceName = "—", topServiceCount = "";
            if (topServiceData != null)
            {
                var svc = await _db.FinancialConstants.FindAsync(topServiceData.Id);
                topServiceName = svc?.service_name ?? $"خدمة {topServiceData.Id}";
                topServiceCount = $"{topServiceData.Count} طلب";
            }

            var serviceMap = await _db.FinancialConstants
                .ToDictionaryAsync<FinancialConstant, int, string>(
                    s => s.service_id,
                    s => s.service_name ?? string.Empty
                );

            var recentPaymentsRaw = await _db.FinancialPayments
                .OrderByDescending(p => p.payment_date).Take(10).ToListAsync();

            var recentPayments = recentPaymentsRaw.Select(p => new DashboardPaymentItemDto
            {
                PaymentId = $"#{p.payment_id}",
                UnitId = p.unit_id.ToString(),
                ServiceName = p.service_id is int pSid
                    ? serviceMap.GetValueOrDefault(pSid, "—")
                    : "—",
                TotalFeeRaw = (decimal)p.total_service_fee,
                TotalFee = ((decimal)p.total_service_fee).ToString("N0") + " د.ع",
                PaymentDateRaw = p.payment_date,
                PaymentDate = p.payment_date.ToString("yyyy/MM/dd"),
                PaymentMethod = p.payment_method
            }).ToList();

            var recentMaintenanceRaw = await _db.MaintenanceRequests
                .OrderByDescending(m => m.request_date).Take(8).ToListAsync();

            var recentMaintenance = recentMaintenanceRaw.Select(m => new DashboardMaintenanceItemDto
            {
                RequestId = $"#{m.request_id}",
                UnitId = m.unit_id.ToString(),
                ServiceName = m.service_id is int mSid
                    ? serviceMap.GetValueOrDefault(mSid, "—")
                    : "—",
                RequestDateRaw = m.request_date,
                RequestDate = m.request_date.ToString("yyyy/MM/dd"),
                Status = m.request_status ?? "غير محدد",
                StatusColor = MapStatusColor(m.request_status)
            }).ToList();

            return Ok(new DashboardResponseDto
            {
                Kpi = new DashboardKpiDto
                {
                    TotalResidents = totalResidents,
                    ResidentsSub = residentsSub,
                    OccupiedUnits = occupiedUnits,
                    TotalUnits = totalUnits,
                    OccupancyRate = $"نسبة الإشغال {occupancyPct}%",
                    PendingVisits = pendingVisits,
                    VisitsSub = $"{todayVisits} زيارة اليوم",
                    OpenMaintenance = openMaintenance,
                    MaintenanceSub = $"{inProgress} قيد التنفيذ",
                    TotalRevenueRaw = totalRevenueRaw,
                    TotalRevenue = totalRevenueRaw.ToString("N0") + " د.ع",
                    MonthlyRevenueRaw = monthlyRevenueRaw,
                    MonthlyRevenue = monthlyRevenueRaw.ToString("N0") + " د.ع",
                    CurrentMonthLabel = $"{arabicMonths[now.Month - 1]} {now.Year}",
                    TopService = topServiceName,
                    TopServiceCount = topServiceCount,
                    RecentPaymentsSub = $"آخر {recentPayments.Count} دفعات",
                    PaymentsCountLabel = $"الإجمالي: {await _db.FinancialPayments.CountAsync()}",
                    EmployeeName = employeeName,
                    EmployeeTitle = employeeTitle
                },
                RecentPayments = recentPayments,
                RecentMaintenance = recentMaintenance
            });
        }

        // ══════════════════════════════════════════════════════════
        //  الموظفون
        // ══════════════════════════════════════════════════════════

        [HttpGet("employees")]
        public async Task<ActionResult> GetEmployees()
        {
            return Ok(await _db.Employees.Select(e => new
            {
                e.employee_id,
                e.employee_code,
                e.first_name,
                e.second_name,
                e.third_name,
                e.job_title,
                e.phone_number,
                e.national_id_front_path,
                e.national_id_back_path,
                full_name = $"{e.first_name} {e.second_name} {e.third_name}".Trim()
            }).ToListAsync());
        }

        [HttpPost("employees")]
        public async Task<ActionResult> AddEmployee([FromBody] AddEmployeeRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.EmployeeCode) || string.IsNullOrWhiteSpace(req.FirstName))
                return BadRequest(new { message = "رمز الموظف والاسم الأول مطلوبان" });

            if (await _db.Employees.AnyAsync(e => e.employee_code == req.EmployeeCode))
                return BadRequest(new { message = "رمز الموظف مستخدم مسبقاً" });

            var emp = new Employee
            {
                employee_code = req.EmployeeCode.Trim(),
                first_name = req.FirstName.Trim(),
                second_name = NullIfEmpty(req.SecondName),
                third_name = NullIfEmpty(req.ThirdName),
                job_title = NullIfEmpty(req.JobTitle),
                phone_number = NullIfEmpty(req.PhoneNumber),
                national_id_front_path = NullIfEmpty(req.NationalIdFrontPath),
                national_id_back_path = NullIfEmpty(req.NationalIdBackPath)
            };

            _db.Employees.Add(emp);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "تمت إضافة الموظف بنجاح",
                employee_id = emp.employee_id,
                employee_code = emp.employee_code,
                full_name = string.Join(" ", new[] { emp.first_name, emp.second_name, emp.third_name }
                                  .Where(n => !string.IsNullOrWhiteSpace(n)))
            });
        }

        [HttpDelete("employees/{id}")]
        public async Task<ActionResult> DeleteEmployee(int id)
        {
            var emp = await _db.Employees.FindAsync(id);
            if (emp == null) return NotFound(new { message = "الموظف غير موجود" });

            _db.Employees.Remove(emp);
            await _db.SaveChangesAsync();

            return Ok(new { message = "تم حذف الموظف بنجاح" });
        }

        // ══════════════════════════════════════════════════════════
        //  القائمة السوداء
        // ══════════════════════════════════════════════════════════

        [HttpGet("blacklist")]
        public async Task<ActionResult> GetBlacklist()
        {
            return Ok(await _db.Blacklist.Select(b => new
            {
                b.blacklist_id,
                b.person_name,
                b.employee_id,
                b.added_date,
                b.reason
            }).ToListAsync());
        }

        [HttpPost("blacklist")]
        public async Task<ActionResult> AddToBlacklist([FromBody] AddBlacklistRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.PersonName))
                return BadRequest(new { message = "اسم الشخص مطلوب" });

            if (!await _db.Employees.AnyAsync(e => e.employee_id == req.EmployeeId))
                return BadRequest(new { message = "الموظف المسؤول غير موجود" });

            var b = new Blacklist
            {
                person_name = req.PersonName.Trim(),
                employee_id = req.EmployeeId,
                reason = NullIfEmpty(req.Reason),
                added_date = DateTime.Now
            };

            _db.Blacklist.Add(b);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "تمت إضافة الشخص للقائمة السوداء بنجاح",
                blacklist_id = b.blacklist_id
            });
        }

        [HttpDelete("blacklist/{id}")]
        public async Task<ActionResult> RemoveFromBlacklist(int id)
        {
            var b = await _db.Blacklist.FindAsync(id);
            if (b == null) return NotFound(new { message = "السجل غير موجود" });

            _db.Blacklist.Remove(b);
            await _db.SaveChangesAsync();

            return Ok(new { message = "تم حذف السجل من القائمة السوداء" });
        }

        // ══════════════════════════════════════════════════════════
        //  السكان
        // ══════════════════════════════════════════════════════════

        [HttpPost("add-resident")]
        public async Task<ActionResult> AddResident([FromBody] AddResidentRequest req)
        {
            var unit = await _db.HousingUnits.FindAsync(req.UnitId);
            if (unit == null) return NotFound(new { message = "الوحدة غير موجودة" });

            var res = new Resident
            {
                unit_id = req.UnitId,
                resident_code = Guid.NewGuid().ToString()[..8].ToUpper(),
                first_name = req.FirstName,
                second_name = req.SecondName,
                third_name = req.ThirdName,
                phone_number = req.PhoneNumber,
                resident_type = req.ResidentType,
                family_members_count = req.FamilyMembersCount
            };

            _db.Residents.Add(res);
            unit.unit_status = "مشغول";
            await _db.SaveChangesAsync();

            return Ok(new { message = "تم التسكين بنجاح", resident_code = res.resident_code });
        }

        // ══════════════════════════════════════════════════════════
        //  الزيارات
        // ══════════════════════════════════════════════════════════

        [HttpGet("pending-visits")]
        public async Task<ActionResult> GetPendingVisits()
            => Ok(await _db.Visits.Where(v => v.visit_status == "معلقة")
                           .OrderByDescending(v => v.visit_date).ToListAsync());

        [HttpPut("approve-visit")]
        public async Task<ActionResult> ApproveVisit([FromBody] ApproveVisitRequest req)
        {
            var visit = await _db.Visits.FindAsync(req.VisitId);
            if (visit == null) return NotFound(new { message = "الزيارة غير موجودة" });

            visit.visit_status = req.NewStatus;
            if (req.ExpiryDate.HasValue) visit.expiry_date = req.ExpiryDate.Value;

            await _db.SaveChangesAsync();
            return Ok(new { message = "تم تحديث حالة الزيارة" });
        }

        // ══════════════════════════════════════════════════════════
        //  الصيانة
        // ══════════════════════════════════════════════════════════

        [HttpPost("create-maintenance")]
        public async Task<ActionResult> CreateMaintenance([FromBody] CreateMaintenanceRequest req)
        {
            var m = new MaintenanceRequest
            {
                unit_id = req.UnitId,
                service_id = req.ServiceId,
                request_date = DateTime.Now,
                request_status = req.RequestStatus ?? "مفتوح",
                feedback = req.Feedback
            };

            _db.MaintenanceRequests.Add(m);
            await _db.SaveChangesAsync();

            return Ok(new { message = "تم إنشاء طلب الصيانة", request_id = m.request_id });
        }

        // ══════════════════════════════════════════════════════════
        //  الخدمات والوحدات
        // ══════════════════════════════════════════════════════════

        [HttpGet("services")]
        public async Task<ActionResult> GetServices()
            => Ok(await _db.FinancialConstants.ToListAsync());

        [HttpGet("available-units")]
        public async Task<ActionResult> GetAvailableUnits()
            => Ok(await _db.HousingUnits.Where(u => u.unit_status == "فارغ").ToListAsync());

        // ══════════════════════════════════════════════════════════
        //  مساعد
        // ══════════════════════════════════════════════════════════
        private static string? NullIfEmpty(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string MapStatusColor(string? status) => status switch
        {
            "مفتوح" => "#E67E22",
            "قيد التنفيذ" => "#3498DB",
            "مكتمل" => "#27AE60",
            "مرفوض" => "#E74C3C",
            _ => "#95A5A6"
        };
    }
}