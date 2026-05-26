using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StarComplexAPI.Data;
using StarComplexAPI.Models;
using Konscious.Security.Cryptography;
using System.Security.Cryptography;
using System.Text;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly StarComplexContext _context;
    private readonly IMemoryCache _cache;

    // ══ ثوابت Argon2id ═══════════════════════════════════════════
    private const int Argon2MemorySize = 16384;
    private const int Argon2Iterations = 2;
    private const int Argon2Parallelism = 1;
    private const int Argon2HashLength = 32;
    private const int SaltSize = 16;

    private const string TypeSecurity = "security";
    private const string TypeAdmin = "admin";

    public AuthController(StarComplexContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    // ══ Argon2id — مساعدات مشتركة ════════════════════════════════

    private static byte[] GenerateSalt()
    {
        byte[] salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    // ── هاش الموظف: "fullName||code" ─────────────────────────────
    private static string ComputeHash(string fullName, string code, byte[] salt)
    {
        string combined = $"{fullName.Trim()}||{code.Trim()}";
        byte[] input = Encoding.UTF8.GetBytes(combined);

        using var argon2 = new Argon2id(input)
        {
            Salt = salt,
            MemorySize = Argon2MemorySize,
            Iterations = Argon2Iterations,
            DegreeOfParallelism = Argon2Parallelism
        };

        byte[] hash = argon2.GetBytes(Argon2HashLength);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyHash(string fullName, string code, string storedHash)
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

    // ── هاش الساكن: "fullName||accessCode" ───────────────────────
    private static string ComputeResidentHash(string fullName, string accessCode, byte[] salt)
    {
        string combined = $"{fullName.Trim()}||{accessCode.Trim()}";
        byte[] input = Encoding.UTF8.GetBytes(combined);

        using var argon2 = new Argon2id(input)
        {
            Salt = salt,
            MemorySize = Argon2MemorySize,
            Iterations = Argon2Iterations,
            DegreeOfParallelism = Argon2Parallelism
        };

        byte[] hash = argon2.GetBytes(Argon2HashLength);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyResidentHash(string fullName, string accessCode, string storedHash)
    {
        try
        {
            string[] parts = storedHash.Split(':');
            if (parts.Length != 2) return false;

            byte[] salt = Convert.FromBase64String(parts[0]);
            byte[] storedBytes = Convert.FromBase64String(parts[1]);

            string combined = $"{fullName.Trim()}||{accessCode.Trim()}";
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

    // ══════════════════════════════════════════════════════════════
    //  مساعد مشترك — تحقق من هوية الموظف (أمن أو إداري)
    //
    //  المنطق:
    //  ┌────────────────────────────────────────────────────────────┐
    //  │ 1. ابحث بالكود                                             │
    //  │ 2. إذا كان النوع مختلف → ارفض                             │
    //  │ 3. إذا كان password_hash موجود → تحقق به                 │
    //  │ 4. إذا password_hash فارغ (أول دخول):                     │
    //  │       a. تحقق نصي من الاسم الكامل                         │
    //  │       b. ولّد Argon2id وحفظ في:                           │
    //  │          - password_hash (الأساسي)                        │
    //  │          - name_hash (بديل)                               │
    //  │          - name_hash_admin (للإداريين فقط)                │
    //  │       c. عيّن employee_type و employee_index              │
    //  │ ✅ بعد أول دخول: تحقق من password_hash                   │
    //  └────────────────────────────────────────────────────────────┘
    // ══════════════════════════════════════════════════════════════
    private async Task<(Employee? employee, string? error)> AuthEmployee(
    string fullName, string code, string requiredType)
    {
        string cleanCode = code.Trim();
        string cleanName = fullName.Trim();

        // ── 1. ابحث بالكود ───────────────────────────────────────
        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => (e.employee_code ?? "").Trim() == cleanCode);

        if (employee == null)
            return (null, "رمز الموظف أو الاسم غير صحيح");

        // ── 2. فحص النوع ─────────────────────────────────────────
        if (!string.IsNullOrEmpty(employee.employee_type) &&
            employee.employee_type != requiredType)
            return (null, "رمز الموظف أو الاسم غير صحيح");

        // ── 3. password_hash موجود → تحقق منه مباشرة ─────────────
        if (!string.IsNullOrEmpty(employee.password_hash))
        {
            bool valid = VerifyHash(cleanName, cleanCode, employee.password_hash);
            if (!valid)
                return (null, "رمز الموظف أو الاسم غير صحيح");
        }
        else
        {
            // ── 4. أول دخول: تحقق نصي من الاسم الكامل ──────────
            string dbFullName = string.Join(" ",
                new[] { employee.first_name, employee.second_name, employee.third_name }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim()));

            string inputName = string.Join(" ",
                cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                         .Select(n => n.Trim()));

            bool namesMatch = string.Equals(
                dbFullName, inputName, StringComparison.CurrentCultureIgnoreCase);

            if (!namesMatch)
                return (null, "رمز الموظف أو الاسم غير صحيح");

            // ── 5. ولّد الهاشات وحفظها ────────────────────────────
            byte[] salt = GenerateSalt();
            string hash = ComputeHash(cleanName, cleanCode, salt);

            // ✅ password_hash — للتحقق الرئيسي
            employee.password_hash = hash;

            // ✅ name_hash — لكلا النوعين (security + admin) بدون name_hash_admin
            employee.name_hash = hash;

            // ✅ عيّن نوع الموظف
            employee.employee_type = requiredType;

            // ✅ عيّن employee_index
            if (string.IsNullOrEmpty(employee.employee_index))
            {
                var maxIndex = await _context.Employees
                    .Where(e => !string.IsNullOrEmpty(e.employee_index))
                    .CountAsync();

                employee.employee_index = (maxIndex + 1).ToString();
            }

            await _context.SaveChangesAsync();
        }

        // ── 6. تحديث إحصاءات الدخول ──────────────────────────────
        employee.login_count = (employee.login_count ?? 0) + 1;
        employee.last_login = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return (employee, null);
    }

    // ══════════════════════════════════════════════════════════════
    //  تسجيل دخول الساكن
    //  POST api/Auth/login
    // ══════════════════════════════════════════════════════════════
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AccessCode))
            return BadRequest(new { message = "يرجى إدخال الرمز السري" });

        if (string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new { message = "يرجى إدخال الاسم الثلاثي" });

        var unit = await _context.HousingUnits
            .FirstOrDefaultAsync(u => u.access_code == request.AccessCode.Trim());

        if (unit == null)
            return Unauthorized(new { message = "الرمز السري أو الاسم غير صحيح" });

        var names = request.FullName.Trim()
                           .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string fName = names.Length > 0 ? names[0] : "ساكن";
        string sName = names.Length > 1 ? names[1] : "";
        string tName = names.Length > 2 ? string.Join(" ", names.Skip(2)) : "";

        Resident? resident = await _context.Residents
            .FirstOrDefaultAsync(r =>
                r.unit_id == unit.unit_id &&
                (r.first_name ?? "").Equals(fName) &&
                (r.second_name ?? "").Equals(sName) &&
                (r.third_name ?? "").Equals(tName));

        if (resident != null)
        {
            if (!string.IsNullOrEmpty(resident.name_hash))
            {
                bool valid = VerifyResidentHash(
                    request.FullName.Trim(),
                    request.AccessCode.Trim(),
                    resident.name_hash);

                if (!valid)
                    return Unauthorized(new { message = "الرمز السري أو الاسم غير صحيح" });
            }
            else
            {
                byte[] newSalt = GenerateSalt();
                resident.name_hash = ComputeResidentHash(
                    request.FullName.Trim(),
                    request.AccessCode.Trim(),
                    newSalt);
            }

            resident.login_count = (resident.login_count ?? 0) + 1;
            resident.last_login = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
        else
        {
            var primaryResident = await _context.Residents
                .Where(r => r.unit_id == unit.unit_id)
                .OrderBy(r => r.resident_id)
                .FirstOrDefaultAsync();

            int currentCount = await _context.Residents
                .CountAsync(r => r.unit_id == unit.unit_id);

            int maxAllowed = (primaryResident?.family_members_count ?? 0) > 0
                ? primaryResident!.family_members_count!.Value
                : 10;

            if (currentCount >= maxAllowed)
                return BadRequest(new
                {
                    message =
                        $"عذراً، تم الوصول للحد الأقصى للمستخدمين لهذه الوحدة ({maxAllowed} أشخاص)"
                });

            byte[] salt = GenerateSalt();
            string nameHash = ComputeResidentHash(
                request.FullName.Trim(),
                request.AccessCode.Trim(),
                salt);

            resident = new Resident
            {
                unit_id = unit.unit_id,
                resident_code = "RES-" + unit.unit_id + "-" +
                                 Guid.NewGuid().ToString()[..4].ToUpper(),
                first_name = fName,
                second_name = sName,
                third_name = tName,
                name_hash = nameHash,
                phone_number = "",
                resident_type = "",
                login_count = 1,
                last_login = DateTime.UtcNow
            };

            _context.Residents.Add(resident);
            await _context.SaveChangesAsync();
        }

        return Ok(new
        {
            message = "تم تسجيل الدخول بنجاح",
            role = "resident",
            unitId = unit.unit_id,
            status = unit.unit_status
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  تسجيل دخول موظف الأمن
    //  POST api/Auth/employee-login
    //  body: { fullName, employeeCode }
    //
    //  ✅ يستقبل الاسم الثلاثي الكامل
    //  ✅ أول دخول: يتحقق نصياً ثم يشفّر
    //  ✅ يحفظ في password_hash + name_hash
    //  ✅ الدخولات التالية: يتحقق من password_hash
    // ══════════════════════════════════════════════════════════════
    [HttpPost("employee-login")]
    public async Task<IActionResult> EmployeeLogin([FromBody] EmployeeLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.EmployeeCode))
            return BadRequest(new { message = "يرجى إدخال الاسم الثلاثي ورمز الموظف" });

        var (employee, error) = await AuthEmployee(
            request.FullName, request.EmployeeCode, TypeSecurity);

        if (employee == null)
            return Unauthorized(new { message = error });

        // ── رفض الموظف الإداري من بوابة الأمن ───────────────────
        string jobTitle = employee.job_title?.Trim() ?? "";
        bool isAdminJob = jobTitle is "موظف اداري" or "موظف إداري" or "مدير" or "مديرة";

        if (isAdminJob)
            return Unauthorized(new
            {
                message = "هذا الحساب مخصص للموظفين الإداريين، يرجى استخدام بوابة الإدارة"
            });

        string dbFullName = string.Join(" ",
            new[] { employee.first_name, employee.second_name, employee.third_name }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim()));

        return Ok(new
        {
            message = "تم تسجيل دخول موظف الأمن بنجاح",
            role = "security",
            employeeCode = employee.employee_code,
            employeeName = dbFullName,
            employeeFullName = dbFullName,
            jobTitle = employee.job_title,
            employeeId = employee.employee_id,
            employeeIndex = employee.employee_index,
            hasFrontId = !string.IsNullOrEmpty(employee.national_id_front_path),
            hasBackId = !string.IsNullOrEmpty(employee.national_id_back_path),
            nationalIdFrontPath = employee.national_id_front_path,
            nationalIdBackPath = employee.national_id_back_path
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  تسجيل دخول الموظف الإداري / المدير
    //  POST api/Auth/admin-login
    //  body: { fullName, employeeCode }
    //
    //  ✅ نفس منطق الأمن لكن requiredType = "admin"
    //  ✅ يرفض موظف الأمن من بوابة الإدارة
    //  ✅ يحفظ في name_hash_admin بالإضافة للحقول الأخرى
    // ══════════════════════════════════════════════════════════════
    [HttpPost("admin-login")]
    public async Task<IActionResult> AdminLogin([FromBody] EmployeeLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.EmployeeCode))
            return BadRequest(new { message = "يرجى إدخال الاسم الثلاثي ورمز الموظف" });

        var (employee, error) = await AuthEmployee(
            request.FullName, request.EmployeeCode, TypeAdmin);

        if (employee == null)
            return Unauthorized(new { message = error });

        // ── رفض موظف الأمن من بوابة الإدارة ─────────────────────
        string jobTitle = employee.job_title?.Trim() ?? "";
        bool isAdminJob = jobTitle is "موظف اداري" or "موظف إداري" or "مدير" or "مديرة";

        if (!isAdminJob)
            return Unauthorized(new
            {
                message = "ليس لديك صلاحية الوصول للوحة الإدارة",
                hint = $"المسمى الوظيفي الحالي: {jobTitle}"
            });

        string dbFullName = string.Join(" ",
            new[] { employee.first_name, employee.second_name, employee.third_name }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim()));

        return Ok(new
        {
            message = "تم تسجيل دخول الموظف الإداري بنجاح",
            role = "admin",
            employeeCode = employee.employee_code,
            employeeName = dbFullName,
            jobTitle = employee.job_title,
            employeeId = employee.employee_id,
            employeeIndex = employee.employee_index,
            hasFrontId = !string.IsNullOrEmpty(employee.national_id_front_path),
            hasBackId = !string.IsNullOrEmpty(employee.national_id_back_path),
            nationalIdFrontPath = employee.national_id_front_path,
            nationalIdBackPath = employee.national_id_back_path
        });
    }
}

// ══ DTOs ══════════════════════════════════════════════════════════
public class LoginRequest
{
    public string FullName { get; set; } = string.Empty;
    public string AccessCode { get; set; } = string.Empty;
}

public class EmployeeLoginRequest
{
    public string FullName { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
}