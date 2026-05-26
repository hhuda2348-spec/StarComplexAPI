using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;

[ApiController]
[Route("api/EmployeeProfile")]
public class EmployeeProfileController : ControllerBase
{
    private readonly StarComplexContext _context;
    private readonly IWebHostEnvironment _env;

    private static readonly HashSet<string> _allowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public EmployeeProfileController(StarComplexContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    // ── GET api/EmployeeProfile/{id} ──────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetEmployee(int id)
    {
        var emp = await _context.Employees.FindAsync(id);
        if (emp == null) return NotFound(new { message = "الموظف غير موجود" });

        return Ok(new EmployeeDto
        {
            employee_id = emp.employee_id,
            employee_code = emp.employee_code,
            first_name = emp.first_name,
            second_name = emp.second_name,
            third_name = emp.third_name,
            job_title = emp.job_title,
            phone_number = emp.phone_number,
            national_id_front_path = emp.national_id_front_path,
            national_id_back_path = emp.national_id_back_path
        });
    }

    // ── PUT api/EmployeeProfile/{id} ──────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateEmployee(int id, [FromBody] EmployeeDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var emp = await _context.Employees.FindAsync(id);
        if (emp == null) return NotFound(new { message = "الموظف غير موجود" });

        emp.first_name = dto.first_name?.Trim();
        emp.second_name = dto.second_name?.Trim();
        emp.third_name = dto.third_name?.Trim();
        emp.job_title = dto.job_title?.Trim();
        emp.phone_number = dto.phone_number?.Trim();

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "تعارض في البيانات، يرجى إعادة المحاولة" });
        }

        return Ok(new EmployeeDto
        {
            employee_id = emp.employee_id,
            employee_code = emp.employee_code,
            first_name = emp.first_name,
            second_name = emp.second_name,
            third_name = emp.third_name,
            job_title = emp.job_title,
            phone_number = emp.phone_number,
            national_id_front_path = emp.national_id_front_path,
            national_id_back_path = emp.national_id_back_path
        });
    }

    // ── POST api/EmployeeProfile/{id}/upload-front ────────────────────────────
    [HttpPost("{id:int}/upload-front")]
    public async Task<IActionResult> UploadFrontId(int id, IFormFile file)
        => await UploadIdImage(id, file, isFront: true);

    // ── POST api/EmployeeProfile/{id}/upload-back ─────────────────────────────
    [HttpPost("{id:int}/upload-back")]
    public async Task<IActionResult> UploadBackId(int id, IFormFile file)
        => await UploadIdImage(id, file, isFront: false);

    // ── Shared upload logic ───────────────────────────────────────────────────
    private async Task<IActionResult> UploadIdImage(int id, IFormFile file, bool isFront)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "الملف فارغ أو غير موجود" });

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { message = "حجم الملف يتجاوز الحد المسموح به (5 ميغابايت)" });

        var ext = Path.GetExtension(file.FileName);
        if (!_allowedExtensions.Contains(ext))
            return BadRequest(new { message = "نوع الملف غير مدعوم، يُسمح فقط بـ JPG و PNG و WEBP" });

        var emp = await _context.Employees.FindAsync(id);
        if (emp == null) return NotFound(new { message = "الموظف غير موجود" });

        // حذف الملف القديم إن وُجد
        var oldPath = isFront ? emp.national_id_front_path : emp.national_id_back_path;
        if (!string.IsNullOrEmpty(oldPath))
        {
            var oldFull = Path.Combine(_env.WebRootPath, oldPath.TrimStart('/'));
            if (System.IO.File.Exists(oldFull))
                System.IO.File.Delete(oldFull);
        }

        // حفظ الملف الجديد
        var folder = Path.Combine(_env.WebRootPath, "EmployeeIds");
        Directory.CreateDirectory(folder);

        var fileName = $"{id}_{(isFront ? "front" : "back")}_{Guid.NewGuid()}{ext}";
        var fullPath = Path.Combine(folder, fileName);
        var relativePath = $"/EmployeeIds/{fileName}";

        using (var stream = new FileStream(fullPath, FileMode.Create))
            await file.CopyToAsync(stream);

        if (isFront) emp.national_id_front_path = relativePath;
        else emp.national_id_back_path = relativePath;

        await _context.SaveChangesAsync();

        return Ok(new { path = relativePath });
    }
}

// ── DTO ───────────────────────────────────────────────────────────────────────
public class EmployeeDto
{
    public int employee_id { get; set; }
    public string? employee_code { get; set; }
    public string? first_name { get; set; }
    public string? second_name { get; set; }
    public string? third_name { get; set; }
    public string? job_title { get; set; }
    public string? phone_number { get; set; }
    public string? national_id_front_path { get; set; }
    public string? national_id_back_path { get; set; }
}