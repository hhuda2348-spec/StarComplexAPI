// StarComplexAPI/Controllers/MaintenanceRequestsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;
using StarComplexAPI.Models;
using System.Text.Json.Serialization;
using train.Models;

namespace StarComplexAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MaintenanceRequestsController : ControllerBase
    {
        private readonly StarComplexContext _context;

        public MaintenanceRequestsController(StarComplexContext context)
        {
            _context = context;
        }

        // ═══════════════════════════════════════════════════════════════
        // GET /api/MaintenanceRequests
        // يرجع جميع الطلبات مع JOIN للخدمات والوحدات
        // ═══════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<ActionResult<IEnumerable<MaintenanceRequestDtoMANAG>>> GetRequests()
        {
            try
            {
                var query = from req in _context.MaintenanceRequests
                            join service in _context.FinancialConstants
                                on req.service_id equals service.service_id
                            join unit in _context.HousingUnits
                                on req.unit_id equals unit.unit_id into unitJoin
                            from unit in unitJoin.DefaultIfEmpty()
                            select new MaintenanceRequestDtoMANAG
                            {
                                request_id = req.request_id,
                                unit_id = req.unit_id,
                                service_id = req.service_id,
                                service_name = service.service_name,
                                service_price = service.service_price,
                                unit_type = unit != null ? unit.unit_type : null,
                                unit_status = unit != null ? unit.unit_status : null,
                                request_date = req.request_date,
                                request_status = req.request_status ?? "قيد الانتظار",
                                feedback = req.feedback ?? string.Empty
                            };

                var result = await query.OrderByDescending(q => q.request_date).ToListAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في استرجاع البيانات", detail = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // GET /api/MaintenanceRequests/services
        // ═══════════════════════════════════════════════════════════════
        [HttpGet("services")]
        public async Task<ActionResult<IEnumerable<ServiceDtoMANAG>>> GetServices()
        {
            try
            {
                var services = await _context.FinancialConstants
                    .OrderBy(s => s.service_id)
                    .Select(s => new ServiceDtoMANAG
                    {
                        ServiceId = s.service_id,
                        ServiceName = s.service_name,
                        ServicePrice = s.service_price
                    })
                    .ToListAsync();

                return Ok(services);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في استرجاع الخدمات", detail = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // POST /api/MaintenanceRequests/create
        // ═══════════════════════════════════════════════════════════════
        [HttpPost("create")]
        public async Task<IActionResult> CreateRequest([FromBody] MaintenanceCreateRequest request)
        {
            if (request == null || request.unit_id <= 0)
                return BadRequest(new { message = "بيانات الطلب غير مكتملة" });

            try
            {
                var unitExists = await _context.HousingUnits
                    .AnyAsync(u => u.unit_id == request.unit_id);
                if (!unitExists)
                    return BadRequest(new { message = "رقم الوحدة غير موجود" });

                var serviceExists = await _context.FinancialConstants
                    .AnyAsync(s => s.service_id == request.service_id);
                if (!serviceExists)
                    return BadRequest(new { message = "نوع الخدمة غير موجود" });

                var maintenanceEntry = new MaintenanceRequest
                {
                    unit_id = request.unit_id,
                    service_id = request.service_id,
                    request_date = DateTime.Now,
                    request_status = "قيد الانتظار",
                    feedback = request.feedback ?? string.Empty
                };

                _context.MaintenanceRequests.Add(maintenanceEntry);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    status = "Success",
                    message = "تم تسجيل الطلب بنجاح",
                    requestId = maintenanceEntry.request_id
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في معالجة الطلب", detail = ex.Message });
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // DTOs — MaintenanceRequestsController
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// DTO يُرسَل للعميل — يحتوي على بيانات من JOIN
    /// </summary>
    public class MaintenanceRequestDtoMANAG
    {
        [JsonPropertyName("request_id")] public int request_id { get; set; }
        [JsonPropertyName("unit_id")] public int unit_id { get; set; }
        [JsonPropertyName("service_id")] public int service_id { get; set; }
        [JsonPropertyName("service_name")] public string service_name { get; set; } = string.Empty;
        [JsonPropertyName("service_price")] public decimal service_price { get; set; }
        [JsonPropertyName("unit_type")] public string? unit_type { get; set; }
        [JsonPropertyName("unit_status")] public string? unit_status { get; set; }
        [JsonPropertyName("request_date")] public DateTime request_date { get; set; }
        [JsonPropertyName("request_status")] public string request_status { get; set; } = string.Empty;
        [JsonPropertyName("feedback")] public string feedback { get; set; } = string.Empty;
    }

    public class ServiceDtoMANAG
    {
        [JsonPropertyName("service_id")] public int ServiceId { get; set; }
        [JsonPropertyName("service_name")] public string ServiceName { get; set; } = string.Empty;
        [JsonPropertyName("service_price")] public decimal ServicePrice { get; set; }
    }

    public class MaintenanceCreateRequest
    {
        [JsonPropertyName("unit_id")] public int unit_id { get; set; }
        [JsonPropertyName("service_id")] public int service_id { get; set; }
        [JsonPropertyName("feedback")] public string feedback { get; set; } = string.Empty;
    }
}