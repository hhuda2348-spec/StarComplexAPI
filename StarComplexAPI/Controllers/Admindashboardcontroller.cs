using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;
using StarComplexAPI.Models;

namespace StarComplexAPI.Controllers
{
    // ══════════════════════════════════════════════════════════════
    //  DTOs
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

    public class ArchivedEmployeeDto
    {
        public int ArchiveId { get; set; }
        public int EmployeeId { get; set; }
        public string? FullName { get; set; }
        public string? FirstName { get; set; }
        public string? SecondName { get; set; }
        public string? ThirdName { get; set; }
        public string? JobTitle { get; set; }
        public string? PhoneNumber { get; set; }
        public DateTime ArchivedAt { get; set; }
        public int FinancialRecordsCount { get; set; }
        public List<ArchivedFinancialRecordDto> FinancialRecords { get; set; } = new();
    }

    public class ArchivedFinancialRecordDto
    {
        public int PaymentId { get; set; }
        public int UnitId { get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public string TotalFee { get; set; } = string.Empty;
        public decimal TotalFeeRaw { get; set; }
        public string PaymentDate { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
    }

    public class AddBlacklistRequest
    {
        public string PersonName { get; set; } = string.Empty;
        public int EmployeeId { get; set; }
        public string? Reason { get; set; }
    }

    public class AddEmployeeRequest
    {
        public string EmployeeCode { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string? SecondName { get; set; }
        public string? ThirdName { get; set; }
        public string? JobTitle { get; set; }
        public string? PhoneNumber { get; set; }
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
        public string? RequestStatus { get; set; } = "قيد الانتظار";
        public string? Feedback { get; set; }
    }

    // ══════════════════════════════════════════════════════════════
    //  نموذج مساعد لقراءة بيانات الأرشيف المالي من Raw SQL
    // ══════════════════════════════════════════════════════════════
    public class ArchiveFinancialRow
    {
        public int ArchiveId { get; set; }
        public int PaymentId { get; set; }
        public int UnitId { get; set; }
        public int ServiceId { get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public decimal TotalFeeRaw { get; set; }
        public DateTime PaymentDate { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("api/[controller]")]
    public class AdminDashboardController : ControllerBase
    {
        private readonly StarComplexContext _db;

        private static readonly string[] ArabicMonths =
        {
            "يناير","فبراير","مارس","أبريل","مايو","يونيو",
            "يوليو","أغسطس","سبتمبر","أكتوبر","نوفمبر","ديسمبر"
        };

        public AdminDashboardController(StarComplexContext db) => _db = db;

        // ══════════════════════════════════════════════════════════
        //  KPI
        // ══════════════════════════════════════════════════════════
        [HttpGet("kpi/{employeeId}")]
        public async Task<ActionResult<DashboardResponseDto>> GetKpi(int employeeId)
        {
            var now = DateTime.Now;

            var employee = await _db.Employees.FindAsync(employeeId);
            var totalResidents = await _db.Residents.CountAsync();
            var totalFamilyMembers = await _db.FamilyMembers.CountAsync();
            var occupiedUnits = await _db.HousingUnits.CountAsync(u => u.unit_status == "مشغول");
            var totalUnits = await _db.HousingUnits.CountAsync();
            var pendingVisits = await _db.Visits.CountAsync(v => v.visit_status == "معلقة");
            var todayVisits = await _db.Visits.CountAsync(v =>
                v.visit_date.HasValue && v.visit_date.Value.Date == DateTime.Today);
            var openMaintenance = await _db.MaintenanceRequests.CountAsync(m => m.request_status == "قيد الانتظار");
            var inProgressMaintenance = await _db.MaintenanceRequests.CountAsync(m => m.request_status == "قيد التنفيذ");
            var totalPaymentCount = await _db.FinancialPayments.CountAsync();

            var allFees = await _db.FinancialPayments
                .AsNoTracking()
                .Select(p => p.total_service_fee)
                .ToListAsync();
            var totalRevenue = allFees.Sum(f => f ?? 0m);

            var monthFees = await _db.FinancialPayments
                .AsNoTracking()
                .Where(p => p.payment_date.Year == now.Year && p.payment_date.Month == now.Month)
                .Select(p => p.total_service_fee)
                .ToListAsync();
            var monthlyRevenue = monthFees.Sum(f => f ?? 0m);

            var serviceMap = await _db.FinancialConstants
                .AsNoTracking()
                .ToDictionaryAsync(s => s.service_id, s => s.service_name);

            var recentPayments = await _db.FinancialPayments
                .AsNoTracking()
                .OrderByDescending(p => p.payment_date)
                .Take(10)
                .ToListAsync();

            var recentMaintenance = await _db.MaintenanceRequests
                .AsNoTracking()
                .OrderByDescending(m => m.request_date)
                .Take(8)
                .ToListAsync();

            var topServiceData = await _db.FinancialPayments
                .AsNoTracking()
                .Where(p => p.service_id.HasValue)
                .GroupBy(p => p.service_id!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .FirstOrDefaultAsync();

            var employeeName = employee is null ? "غير معروف"
                : string.Join(" ", new[] { employee.first_name, employee.second_name, employee.third_name }
                    .Where(n => !string.IsNullOrWhiteSpace(n)));

            var occupancyPercentage = totalUnits > 0
                ? (int)Math.Round((double)occupiedUnits / totalUnits * 100) : 0;

            string topServiceName = "—", topServiceCount = "";
            if (topServiceData != null)
            {
                topServiceName = serviceMap.GetValueOrDefault(topServiceData.Id, $"خدمة {topServiceData.Id}");
                topServiceCount = $"{topServiceData.Count} طلب";
            }

            var paymentDtos = recentPayments.Select(p => new DashboardPaymentItemDto
            {
                PaymentId = $"#{p.payment_id}",
                UnitId = p.unit_id.ToString(),
                ServiceName = p.service_id.HasValue
                    ? serviceMap.GetValueOrDefault(p.service_id.Value, "—") : "—",
                TotalFeeRaw = p.total_service_fee ?? 0m,
                TotalFee = (p.total_service_fee ?? 0m).ToString("N0") + " د.ع",
                PaymentDateRaw = p.payment_date,
                PaymentDate = p.payment_date.ToString("yyyy/MM/dd"),
                PaymentMethod = p.payment_method ?? string.Empty
            }).ToList();

            var maintenanceDtos = recentMaintenance.Select(m => new DashboardMaintenanceItemDto
            {
                RequestId = $"#{m.request_id}",
                UnitId = m.unit_id.ToString(),
                ServiceName = serviceMap.GetValueOrDefault(m.service_id, "—"),
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
                    ResidentsSub = $"مع {totalFamilyMembers} فرد عائلة",
                    OccupiedUnits = occupiedUnits,
                    TotalUnits = totalUnits,
                    OccupancyRate = $"نسبة الإشغال {occupancyPercentage}%",
                    PendingVisits = pendingVisits,
                    VisitsSub = $"{todayVisits} زيارة اليوم",
                    OpenMaintenance = openMaintenance,
                    MaintenanceSub = $"{inProgressMaintenance} قيد التنفيذ",
                    TotalRevenueRaw = totalRevenue,
                    TotalRevenue = totalRevenue.ToString("N0") + " د.ع",
                    MonthlyRevenueRaw = monthlyRevenue,
                    MonthlyRevenue = monthlyRevenue.ToString("N0") + " د.ع",
                    CurrentMonthLabel = $"{ArabicMonths[now.Month - 1]} {now.Year}",
                    TopService = topServiceName,
                    TopServiceCount = topServiceCount,
                    RecentPaymentsSub = $"آخر {paymentDtos.Count} دفعات",
                    PaymentsCountLabel = $"الإجمالي: {totalPaymentCount}",
                    EmployeeName = employeeName,
                    EmployeeTitle = employee?.job_title ?? string.Empty
                },
                RecentPayments = paymentDtos,
                RecentMaintenance = maintenanceDtos
            });
        }

        [HttpGet("full/{employeeId}")]
        public Task<ActionResult<DashboardResponseDto>> GetFullDashboard(int employeeId)
            => GetKpi(employeeId);

        // ══════════════════════════════════════════════════════════
        //  الموظفون
        // ══════════════════════════════════════════════════════════
        [HttpGet("employees")]
        public async Task<ActionResult> GetEmployees()
        {
            var list = await _db.Employees
                .AsNoTracking()
                .Select(e => new
                {
                    e.employee_id,
                    e.employee_code,
                    e.first_name,
                    e.second_name,
                    e.third_name,
                    e.job_title,
                    e.phone_number,
                    e.employee_type,
                    e.employee_index,
                    full_name = (e.first_name + " " +
                                (e.second_name ?? "") + " " +
                                (e.third_name ?? "")).Trim()
                }).ToListAsync();

            return Ok(list);
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
                phone_number = NullIfEmpty(req.PhoneNumber)
            };

            _db.Employees.Add(emp);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "تمت إضافة الموظف بنجاح",
                employee_id = emp.employee_id,
                employee_code = emp.employee_code,
                full_name = string.Join(" ",
                    new[] { emp.first_name, emp.second_name, emp.third_name }
                        .Where(n => !string.IsNullOrWhiteSpace(n)))
            });
        }

        // ══════════════════════════════════════════════════════════
        //  حذف موظف — مع أرشفة كاملة (مصحح لـ MySQL)
        //
        //  ✅ الإصلاحات:
        //    1) استخدام EF مباشرة لـ INSERT بدل ExecuteSqlRawAsync
        //       (يحل مشكلة {0} vs @p0 في MySQL)
        //    2) حذف الموظف عبر EF بدل Raw SQL
        //    3) UPDATE blacklist و financial_payments بـ @p0 (MySQL syntax)
        // ══════════════════════════════════════════════════════════
        [HttpDelete("employees/{id:int}")]
        public async Task<ActionResult> DeleteEmployee(int id, [FromQuery] int requestingEmployeeId)
        {
            if (id == requestingEmployeeId)
                return BadRequest(new { message = "لا يمكنك حذف حسابك الخاص" });

            var emp = await _db.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.employee_id == id);

            if (emp == null)
                return NotFound(new { message = "الموظف غير موجود" });

            // ── جلب خريطة الخدمات ─────────────────────────────────
            var serviceMap = await _db.FinancialConstants
                .AsNoTracking()
                .ToDictionaryAsync(s => s.service_id, s => s.service_name);

            // ── جلب السجلات المالية قبل أي تعديل ─────────────────
            var financialRecords = await _db.FinancialPayments
                .AsNoTracking()
                .Where(p => p.employee_id != null && (int)p.employee_id == id)
                .OrderByDescending(p => p.payment_date)
                .ToListAsync();

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // ── الخطوة 1: أرشفة الموظف عبر EF (يعمل مع MySQL) ─
                var archive = new EmployeeArchive
                {
                    employee_id = emp.employee_id,
                    first_name = emp.first_name,
                    second_name = emp.second_name,
                    third_name = emp.third_name,
                    job_title = emp.job_title,
                    phone_number = emp.phone_number,
                    archived_at = DateTime.Now
                };
                _db.EmployeesArchive.Add(archive);
                await _db.SaveChangesAsync(); // ← يولّد archive_id تلقائياً

                int archiveId = archive.archive_id;
                System.Diagnostics.Debug.WriteLine($"✅ Archive created: archive_id={archiveId}");

                // ── الخطوة 2: حفظ السجلات المالية ─────────────────
                // ✅ MySQL parameters تستخدم @p0, @p1 ... وليس {0}, {1}
                foreach (var p in financialRecords)
                {
                    string svcName = p.service_id.HasValue
                        ? serviceMap.GetValueOrDefault(p.service_id.Value, "—")
                        : "—";

                    await _db.Database.ExecuteSqlRawAsync(@"
                        INSERT INTO employee_archive_financials
                            (archive_id, payment_id, unit_id, service_id,
                             service_name, total_fee, payment_date, payment_method)
                        VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7)",
                        archiveId,
                        p.payment_id,
                        p.unit_id,
                        (object?)p.service_id ?? DBNull.Value,
                        svcName,
                        p.total_service_fee ?? 0m,
                        p.payment_date,
                        (object?)p.payment_method ?? DBNull.Value);
                }

                System.Diagnostics.Debug.WriteLine($"✅ Financial records archived: {financialRecords.Count}");

                // ── الخطوة 3: فك ارتباط financial_payments ─────────
                await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE financial_payments SET employee_id = NULL WHERE employee_id = @p0",
                    id);

                // ── الخطوة 4: فك ارتباط blacklist ──────────────────
                await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE blacklist SET employee_id = 0 WHERE employee_id = @p0",
                    id);

                // ── الخطوة 5: حذف الموظف عبر EF ────────────────────
                var empToDelete = await _db.Employees.FindAsync(id);
                if (empToDelete == null)
                    throw new Exception("الموظف غير موجود عند محاولة الحذف");

                _db.Employees.Remove(empToDelete);
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();
                System.Diagnostics.Debug.WriteLine($"✅ Employee {id} deleted and archived successfully");

                // ── بناء snapshot للـ response ───────────────────────
                var snapshot = financialRecords.Select(p => new ArchivedFinancialRecordDto
                {
                    PaymentId = p.payment_id,
                    UnitId = p.unit_id,
                    ServiceName = p.service_id.HasValue
                        ? serviceMap.GetValueOrDefault(p.service_id.Value, "—") : "—",
                    TotalFeeRaw = p.total_service_fee ?? 0m,
                    TotalFee = (p.total_service_fee ?? 0m).ToString("N0") + " د.ع",
                    PaymentDate = p.payment_date.ToString("yyyy/MM/dd"),
                    PaymentMethod = p.payment_method ?? string.Empty
                }).ToList();

                return Ok(new
                {
                    message = "تم حذف الموظف وأرشفة بياناته بنجاح",
                    archive_id = archiveId,
                    financial_records_kept = financialRecords.Count,
                    financial_snapshot = snapshot
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                System.Diagnostics.Debug.WriteLine($"❌ DeleteEmployee Error: {ex.Message}\n{ex.StackTrace}");
                return StatusCode(500, new { message = $"فشل العملية: {ex.Message}" });
            }
        }

        // ══════════════════════════════════════════════════════════
        //  الأرشيف — مصحح لـ MySQL
        //
        //  ✅ الإصلاح: بدل SqlQueryRaw مع IN ({0}) الخاطئ،
        //     نبني idList كـ string أرقام آمنة مباشرة في الـ query
        //     (آمن لأن archiveIds أرقام int فقط — لا SQL injection)
        // ══════════════════════════════════════════════════════════
        [HttpGet("employees/archive")]
        public async Task<ActionResult> GetArchivedEmployees()
        {
            var archived = await _db.EmployeesArchive
                .AsNoTracking()
                .OrderByDescending(a => a.archived_at)
                .ToListAsync();

            if (archived.Count == 0)
                return Ok(new List<ArchivedEmployeeDto>());

            var archiveIds = archived.Select(a => a.archive_id).ToList();

            // ✅ MySQL fix: بناء IN clause مباشرة بالأرقام (آمن — int فقط)
            var idList = string.Join(",", archiveIds);

            List<ArchiveFinancialRow> financialRows;

            try
            {
                financialRows = await _db.Database
                    .SqlQueryRaw<ArchiveFinancialRow>($@"
                        SELECT
                            archive_id                       AS ArchiveId,
                            payment_id                       AS PaymentId,
                            unit_id                          AS UnitId,
                            COALESCE(service_id, 0)          AS ServiceId,
                            COALESCE(service_name, '—')      AS ServiceName,
                            COALESCE(total_fee, 0)           AS TotalFeeRaw,
                            payment_date                     AS PaymentDate,
                            COALESCE(payment_method, '')     AS PaymentMethod
                        FROM employee_archive_financials
                        WHERE archive_id IN ({idList})")
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ GetArchivedEmployees financials error: {ex.Message}");
                financialRows = new List<ArchiveFinancialRow>();
            }

            // تجميع السجلات المالية حسب archive_id
            var financialByArchive = financialRows
                .GroupBy(r => r.ArchiveId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = archived.Select(a =>
            {
                var records = financialByArchive
                    .GetValueOrDefault(a.archive_id, new List<ArchiveFinancialRow>());

                return new ArchivedEmployeeDto
                {
                    ArchiveId = a.archive_id,
                    EmployeeId = a.employee_id,
                    FullName = string.Join(" ",
                        new[] { a.first_name, a.second_name, a.third_name }
                            .Where(n => !string.IsNullOrWhiteSpace(n))),
                    FirstName = a.first_name,
                    SecondName = a.second_name,
                    ThirdName = a.third_name,
                    JobTitle = a.job_title,
                    PhoneNumber = a.phone_number,
                    ArchivedAt = a.archived_at,
                    FinancialRecordsCount = records.Count,
                    FinancialRecords = records.Select(r => new ArchivedFinancialRecordDto
                    {
                        PaymentId = r.PaymentId,
                        UnitId = r.UnitId,
                        ServiceName = r.ServiceName,
                        TotalFeeRaw = r.TotalFeeRaw,
                        TotalFee = r.TotalFeeRaw.ToString("N0") + " د.ع",
                        PaymentDate = r.PaymentDate.ToString("yyyy/MM/dd"),
                        PaymentMethod = r.PaymentMethod
                    }).ToList()
                };
            }).ToList();

            return Ok(result);
        }

        [HttpGet("employees/archive/{archiveId:int}")]
        public async Task<ActionResult> GetArchivedEmployee(int archiveId)
        {
            var a = await _db.EmployeesArchive
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.archive_id == archiveId);

            if (a == null)
                return NotFound(new { message = "السجل غير موجود في الأرشيف" });

            // ✅ هنا parameter واحد فيعمل بشكل صحيح
            List<ArchiveFinancialRow> financialRows;
            try
            {
                financialRows = await _db.Database
                    .SqlQueryRaw<ArchiveFinancialRow>($@"
                        SELECT
                            archive_id                       AS ArchiveId,
                            payment_id                       AS PaymentId,
                            unit_id                          AS UnitId,
                            COALESCE(service_id, 0)          AS ServiceId,
                            COALESCE(service_name, '—')      AS ServiceName,
                            COALESCE(total_fee, 0)           AS TotalFeeRaw,
                            payment_date                     AS PaymentDate,
                            COALESCE(payment_method, '')     AS PaymentMethod
                        FROM employee_archive_financials
                        WHERE archive_id = {archiveId}")
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ GetArchivedEmployee financials error: {ex.Message}");
                financialRows = new List<ArchiveFinancialRow>();
            }

            return Ok(new
            {
                archiveId = a.archive_id,
                employeeId = a.employee_id,
                fullName = string.Join(" ",
                    new[] { a.first_name, a.second_name, a.third_name }
                        .Where(n => !string.IsNullOrWhiteSpace(n))),
                firstName = a.first_name,
                secondName = a.second_name,
                thirdName = a.third_name,
                jobTitle = a.job_title,
                phoneNumber = a.phone_number,
                archivedAt = a.archived_at,
                financialCount = financialRows.Count,
                financialRecords = financialRows.Select(r => new
                {
                    payment_id = r.PaymentId,
                    unit_id = r.UnitId,
                    serviceName = r.ServiceName,
                    totalFee = r.TotalFeeRaw.ToString("N0") + " د.ع",
                    paymentDate = r.PaymentDate.ToString("yyyy/MM/dd"),
                    payment_method = r.PaymentMethod
                }).ToList()
            });
        }

        // ══════════════════════════════════════════════════════════
        //  القائمة السوداء
        // ══════════════════════════════════════════════════════════
        [HttpGet("blacklist")]
        public async Task<ActionResult> GetBlacklist()
        {
            var list = await _db.Blacklist.AsNoTracking().ToListAsync();

            var empList = await _db.Employees
                .AsNoTracking()
                .Select(e => new
                {
                    e.employee_id,
                    FullName = (e.first_name + " " +
                               (e.second_name ?? "") + " " +
                               (e.third_name ?? "")).Trim()
                }).ToListAsync();

            var archList = await _db.EmployeesArchive
                .AsNoTracking()
                .Select(a => new
                {
                    a.employee_id,
                    FullName = (a.first_name + " " +
                               (a.second_name ?? "") + " " +
                               (a.third_name ?? "")).Trim() + " (مؤرشف)"
                }).ToListAsync();

            var nameMap = new Dictionary<int, string>();
            foreach (var arch in archList)
                nameMap.TryAdd(arch.employee_id, arch.FullName);
            foreach (var e in empList)
                nameMap[e.employee_id] = e.FullName;

            return Ok(list.Select(b => new
            {
                b.blacklist_id,
                person_name = b.person_name ?? string.Empty,
                b.employee_id,
                added_by_name = nameMap.GetValueOrDefault(b.employee_id, "موظف غير معروف"),
                b.added_date,
                b.reason
            }));
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
            if (b == null)
                return NotFound(new { message = "السجل غير موجود" });

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
            if (unit == null)
                return NotFound(new { message = "الوحدة غير موجودة" });

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
            => Ok(await _db.Visits
                .AsNoTracking()
                .Where(v => v.visit_status == "معلقة")
                .OrderByDescending(v => v.visit_date)
                .ToListAsync());

        [HttpPut("approve-visit")]
        public async Task<ActionResult> ApproveVisit([FromBody] ApproveVisitRequest req)
        {
            var visit = await _db.Visits.FindAsync((int?)req.VisitId);
            if (visit == null)
                return NotFound(new { message = "الزيارة غير موجودة" });

            visit.visit_status = req.NewStatus;
            if (req.ExpiryDate.HasValue)
                visit.expiry_date = req.ExpiryDate.Value;

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
                request_status = req.RequestStatus ?? "قيد الانتظار",
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
            => Ok(await _db.FinancialConstants.AsNoTracking().ToListAsync());

        [HttpGet("available-units")]
        public async Task<ActionResult> GetAvailableUnits()
            => Ok(await _db.HousingUnits.AsNoTracking()
                .Where(u => u.unit_status == "فارغ")
                .ToListAsync());

        // ══════════════════════════════════════════════════════════
        //  مساعدات
        // ══════════════════════════════════════════════════════════
        private static string? NullIfEmpty(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string MapStatusColor(string? status) => status switch
        {
            "قيد الانتظار" => "#E67E22",
            "قيد التنفيذ" => "#3498DB",
            "تم تنفيذ الطلب" => "#27AE60",
            "لم يتم تنفيذه" => "#E74C3C",
            _ => "#95A5A6"
        };
    }
}