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

    private const int Argon2MemorySize = 16384;
    private const int Argon2Iterations = 2;
    private const int Argon2Parallelism = 1;
    private const int Argon2HashLength = 32;
    private const int SaltSize = 16;

    public AuthController(StarComplexContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    // ══ Helpers ════════════════════════════════════════════════════════════

    private static byte[] GenerateSalt()
    {
        var salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    private static string NormaliseName(string name)
        => string.Join(" ",
               name.Trim()
                   .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                   .Select(p => p.Trim()));

    /// <summary>
    /// تطبيع شامل للنص العربي — يوحّد الهمزات والياء والتاء والتشكيل
    /// </summary>
    private static string NormaliseArabic(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = NormaliseName(s);
        // توحيد الهمزات
        s = s.Replace("أ", "ا").Replace("إ", "ا").Replace("آ", "ا")
             .Replace("ؤ", "و").Replace("ئ", "ي");
        // توحيد التاء المربوطة والهاء
        s = s.Replace("ة", "ه");
        // توحيد الياء (فارسية → عربية)
        s = s.Replace("ي", "ي").Replace("\u06CC", "ي").Replace("\u0649", "ي");
        // توحيد الكاف الفارسية
        s = s.Replace("\u06A9", "ك");
        // إزالة التشكيل (U+064B → U+065F)
        s = new string(s.Where(c => c < '\u064B' || c > '\u065F').ToArray());
        return s.Trim();
    }

    private static bool ArabicNamesEqual(string a, string b)
        => string.Equals(NormaliseArabic(a), NormaliseArabic(b), StringComparison.Ordinal);

    /// <summary>
    /// التحقق من نوع الموظف — يقبل امن / security / موظف امن
    /// </summary>
    private static bool IsSecurityEmployeeType(string? employeeType)
    {
        if (string.IsNullOrWhiteSpace(employeeType)) return false;
        var t = NormaliseArabic(employeeType.Trim().ToLower());
        return t == "امن" || t == "security" || t.Contains("امن");
    }

    /// <summary>
    /// التحقق من نوع الموظف الإداري — يقبل ادار / admin / مدير
    /// </summary>
    private static bool IsAdminEmployeeType(string? employeeType)
    {
        if (string.IsNullOrWhiteSpace(employeeType)) return false;
        var t = NormaliseArabic(employeeType.Trim().ToLower());
        return t == "اداري" || t == "admin" || t == "مدير" || t.Contains("اداري");
    }

    /// <summary>
    /// التحقق من المسمى الوظيفي الإداري — يتجاهل كل اختلافات الكتابة
    /// </summary>
    private static bool IsAdminJobTitle(string? jobTitle)
    {
        if (string.IsNullOrWhiteSpace(jobTitle)) return false;
        var j = NormaliseArabic(jobTitle);
        return j.Contains("اداري") || j == "مدير" || j == "مديره" || j.StartsWith("مدير");
    }

    private static bool IsSecurityJobTitle(string? jobTitle)
    {
        if (string.IsNullOrWhiteSpace(jobTitle)) return false;
        var j = NormaliseArabic(jobTitle);
        return j.Contains("امن") || j.Contains("حراسه") || j.Contains("حراسة") || j == "امن" || j == "موظف امن";
    }

    // ── Argon2id hash — للسكان فقط ────────────────────────────────────────
    private static string ComputeHash(string fullName, string code, byte[] salt)
    {
        var combined = $"{NormaliseArabic(fullName)}||{code.Trim()}";
        var input = Encoding.UTF8.GetBytes(combined);
        using var a = new Argon2id(input)
        {
            Salt = salt,
            MemorySize = Argon2MemorySize,
            Iterations = Argon2Iterations,
            DegreeOfParallelism = Argon2Parallelism
        };
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(a.GetBytes(Argon2HashLength))}";
    }

    private static bool VerifyHash(string fullName, string code, string storedHash)
    {
        try
        {
            var parts = storedHash.Split(':');
            if (parts.Length != 2) return false;
            var salt = Convert.FromBase64String(parts[0]);
            var storedBytes = Convert.FromBase64String(parts[1]);

            var combined = $"{NormaliseArabic(fullName)}||{code.Trim()}";
            var input = Encoding.UTF8.GetBytes(combined);
            using var a = new Argon2id(input)
            {
                Salt = salt,
                MemorySize = Argon2MemorySize,
                Iterations = Argon2Iterations,
                DegreeOfParallelism = Argon2Parallelism
            };
            return CryptographicOperations.FixedTimeEquals(a.GetBytes(Argon2HashLength), storedBytes);
        }
        catch { return false; }
    }

    // ══ Employee authenticator — بدون تشفير، مقارنة مباشرة للاسم ══════════
    private async Task<(Employee? employee, string? error)> AuthEmployee(
        string fullName, string code)
    {
        var cleanCode = code.Trim();
        var cleanName = NormaliseArabic(fullName);

        // 1. البحث بالكود
        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.employee_code == cleanCode);

        if (employee == null)
            employee = await _context.Employees
                .FirstOrDefaultAsync(e => EF.Functions.Like(e.employee_code, cleanCode));

        if (employee == null)
        {
            Console.WriteLine($"[AUTH] Employee not found: code='{cleanCode}'");
            return (null, "رمز الموظف أو الاسم غير صحيح");
        }

        // 2. مقارنة الاسم مباشرة مع قاعدة البيانات — بدون تشفير
        var dbFullName = NormaliseArabic(string.Join(" ",
            new[] { employee.first_name, employee.second_name, employee.third_name }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())));

        Console.WriteLine($"[AUTH] DB (Normalised)='{dbFullName}' | Input (Normalised)='{cleanName}'");

        if (!string.Equals(dbFullName, cleanName, StringComparison.Ordinal))
        {
            Console.WriteLine($"[AUTH] Name mismatch | DB='{dbFullName}' | Input='{cleanName}'");
            return (null, "رمز الموظف أو الاسم غير صحيح");
        }

        // 3. مسح أي هاش قديم — لا نحتاجه للموظفين
        if (!string.IsNullOrEmpty(employee.password_hash))
        {
            employee.password_hash = null;
            employee.name_hash = null;
        }

        // 4. تعيين employee_index إذا لم يكن موجوداً
        if (string.IsNullOrEmpty(employee.employee_index))
        {
            var count = await _context.Employees
                .CountAsync(e => !string.IsNullOrEmpty(e.employee_index));
            employee.employee_index = (count + 1).ToString();
        }

        // 5. تحديث إحصائيات الدخول
        employee.login_count = (employee.login_count ?? 0) + 1;
        employee.last_login = DateTime.UtcNow;

        // 6. حفظ مع معالجة Concurrency
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            Console.WriteLine($"[AUTH] Concurrency conflict, retrying...");
            foreach (var entry in ex.Entries)
                await entry.ReloadAsync();

            employee.login_count = (employee.login_count ?? 0) + 1;
            employee.last_login = DateTime.UtcNow;

            try { await _context.SaveChangesAsync(); }
            catch (Exception ex2)
            {
                Console.WriteLine($"[AUTH] Save failed (non-critical): {ex2.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUTH] Save failed (non-critical): {ex.Message}");
        }

        return (employee, null);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  POST api/Auth/login   — Resident login
    // ══════════════════════════════════════════════════════════════════════
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
        var fName = names.Length > 0 ? names[0] : "ساكن";
        var sName = names.Length > 1 ? names[1] : "";
        var tName = names.Length > 2 ? string.Join(" ", names.Skip(2)) : "";

        Resident? resident = await _context.Residents
     .FirstOrDefaultAsync(r =>
         r.unit_id == unit.unit_id &&
         (r.first_name ?? "") == fName &&
         (r.second_name ?? "") == sName &&
         (r.third_name ?? "") == tName);
        if (resident != null)
        {
            if (!string.IsNullOrEmpty(resident.name_hash))
            {
                if (!VerifyResidentHash(request.FullName.Trim(),
                                        request.AccessCode.Trim(),
                                        resident.name_hash))
                    return Unauthorized(new { message = "الرمز السري أو الاسم غير صحيح" });
            }
            else
            {
                var newSalt = GenerateSalt();
                resident.name_hash = ComputeResidentHash(
                    request.FullName.Trim(), request.AccessCode.Trim(), newSalt);
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

            var currentCount = await _context.Residents
                .CountAsync(r => r.unit_id == unit.unit_id);

            var maxAllowed = (primaryResident?.family_members_count ?? 0) > 0
                ? primaryResident!.family_members_count!.Value
                : 10;

            if (currentCount >= maxAllowed)
                return BadRequest(new
                {
                    message = $"عذراً، تم الوصول للحد الأقصى للمستخدمين لهذه الوحدة ({maxAllowed} أشخاص)"
                });

            var salt = GenerateSalt();
            var nameHash = ComputeResidentHash(
                request.FullName.Trim(), request.AccessCode.Trim(), salt);

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

    // ── Resident hash helpers ──────────────────────────────────────────────
    private static string ComputeResidentHash(string fullName, string accessCode, byte[] salt)
        => ComputeHash(fullName, accessCode, salt);

    private static bool VerifyResidentHash(string fullName, string accessCode, string storedHash)
        => VerifyHash(fullName, accessCode, storedHash);

    // ══════════════════════════════════════════════════════════════════════
    //  POST api/Auth/employee-login   — Security employee
    // ══════════════════════════════════════════════════════════════════════
    [HttpPost("employee-login")]
    public async Task<IActionResult> EmployeeLogin([FromBody] EmployeeLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.EmployeeCode))
            return BadRequest(new { message = "يرجى إدخال الاسم الثلاثي ورمز الموظف" });

        var (employee, error) = await AuthEmployee(request.FullName, request.EmployeeCode);
        if (employee == null)
            return Unauthorized(new { message = error });

        var jobTitle = employee.job_title?.Trim() ?? "";
        var empType = employee.employee_type?.Trim() ?? "";

        // ── يُرفض لو كان إداري (job_title أو employee_type) ──────────────
        if (IsAdminJobTitle(jobTitle) || IsAdminEmployeeType(empType))
            return Unauthorized(new
            {
                message = "هذا الحساب مخصص للموظفين الإداريين، يرجى استخدام بوابة الإدارة"
            });

        // ── يجب أن يكون موظف أمن (job_title أو employee_type) ────────────
        if (!IsSecurityJobTitle(jobTitle) && !IsSecurityEmployeeType(empType))
            return Unauthorized(new
            {
                message = "ليس لديك صلاحية الوصول لبوابة الأمن\nيجب أن يكون نوع الموظف: امن أو المسمى الوظيفي: موظف امن"
            });

        var dbFullName = NormaliseName(string.Join(" ",
            new[] { employee.first_name, employee.second_name, employee.third_name }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())));

        return Ok(new
        {
            message = "تم تسجيل دخول موظف الأمن بنجاح",
            role = "security",
            employeeCode = employee.employee_code,
            employeeName = dbFullName,
            employeeFullName = dbFullName,
            jobTitle = employee.job_title,
            employeeType = employee.employee_type,
            employeeId = employee.employee_id,
            employeeIndex = employee.employee_index,
            hasFrontId = !string.IsNullOrEmpty(employee.national_id_front_path),
            hasBackId = !string.IsNullOrEmpty(employee.national_id_back_path),
            nationalIdFrontPath = employee.national_id_front_path,
            nationalIdBackPath = employee.national_id_back_path
        });
    }

    // ══════════════════════════════════════════════════════════════════════
    //  POST api/Auth/admin-login   — Admin / manager
    // ══════════════════════════════════════════════════════════════════════
    [HttpPost("admin-login")]
    public async Task<IActionResult> AdminLogin([FromBody] EmployeeLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.EmployeeCode))
            return BadRequest(new { message = "يرجى إدخال الاسم الثلاثي ورمز الموظف" });

        var (employee, error) = await AuthEmployee(request.FullName, request.EmployeeCode);
        if (employee == null)
            return Unauthorized(new { message = error });

        var jobTitle = employee.job_title?.Trim() ?? "";
        var empType = employee.employee_type?.Trim() ?? "";

        // ── يجب أن يكون إداري (job_title أو employee_type) ───────────────
        if (!IsAdminJobTitle(jobTitle) && !IsAdminEmployeeType(empType))
            return Unauthorized(new
            {
                message = "ليس لديك صلاحية الوصول للوحة الإدارة\nيجب أن يكون نوع الموظف: ادار أو المسمى الوظيفي: موظف اداري أو مدير",
                hint = $"نوع الموظف الحالي: '{empType}' | المسمى الوظيفي الحالي: '{jobTitle}'"
            });

        var dbFullName = NormaliseName(string.Join(" ",
            new[] { employee.first_name, employee.second_name, employee.third_name }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())));

        return Ok(new
        {
            message = "تم تسجيل دخول الموظف الإداري بنجاح",
            role = "admin",
            employeeCode = employee.employee_code,
            employeeName = dbFullName,
            employeeFullName = dbFullName,
            jobTitle = employee.job_title,
            employeeType = employee.employee_type,
            employeeId = employee.employee_id,
            employeeIndex = employee.employee_index,
            hasFrontId = !string.IsNullOrEmpty(employee.national_id_front_path),
            hasBackId = !string.IsNullOrEmpty(employee.national_id_back_path),
            nationalIdFrontPath = employee.national_id_front_path,
            nationalIdBackPath = employee.national_id_back_path
        });
    }

    // ══════════════════════════════════════════════════════════════════════
    //  POST api/Auth/debug-employee   — للتشخيص فقط
    // ══════════════════════════════════════════════════════════════════════
    [HttpPost("debug-employee")]
    public async Task<IActionResult> DebugEmployee([FromBody] EmployeeLoginRequest request)
    {
        var cleanCode = request.EmployeeCode.Trim();
        var cleanName = NormaliseName(request.FullName);

        var allEmployees = await _context.Employees.ToListAsync();
        var codeMatch = allEmployees.FirstOrDefault(e =>
            (e.employee_code ?? "").Trim() == cleanCode);

        if (codeMatch == null)
            return Ok(new
            {
                found = false,
                totalEmployees = allEmployees.Count,
                sampleCodes = allEmployees.Take(5).Select(e => new
                {
                    raw = e.employee_code,
                    length = e.employee_code?.Length
                })
            });

        var dbFullName = NormaliseName(string.Join(" ",
            new[] { codeMatch.first_name, codeMatch.second_name, codeMatch.third_name }
                .Where(n => !string.IsNullOrWhiteSpace(n))));

        return Ok(new
        {
            found = true,
            dbName = dbFullName,
            dbNameNormalised = NormaliseArabic(dbFullName),
            inputName = cleanName,
            inputNameNormalised = NormaliseArabic(cleanName),
            namesMatch = ArabicNamesEqual(dbFullName, cleanName),
            hasHash = !string.IsNullOrEmpty(codeMatch.password_hash),
            jobTitle = codeMatch.job_title,
            jobTitleNormalised = NormaliseArabic(codeMatch.job_title ?? ""),
            employeeType = codeMatch.employee_type,
            employeeTypeNorm = NormaliseArabic(codeMatch.employee_type ?? ""),
            isAdmin = IsAdminJobTitle(codeMatch.job_title) || IsAdminEmployeeType(codeMatch.employee_type),
            isSecurity = IsSecurityJobTitle(codeMatch.job_title) || IsSecurityEmployeeType(codeMatch.employee_type)
        });
    }
}

// ══ DTOs ══════════════════════════════════════════════════════════════════
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
