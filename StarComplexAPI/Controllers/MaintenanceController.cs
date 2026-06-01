using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarComplexAPI.Data;
using StarComplexAPI.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using train.Models;

namespace StarComplexAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MaintenanceController : ControllerBase
    {
        private readonly StarComplexContext _context;

        public MaintenanceController(StarComplexContext context)
        {
            _context = context;
        }

        // --- 1. قسم طلبات الصيانة ---

        /// <summary>استرجاع كافة طلبات الصيانة الخاصة برقم وحدة معينة</summary>
        [HttpGet("GetRequests/{unitId}")]
        public async Task<IActionResult> GetRequests(int unitId)
        {
            try
            {
                var requests = await _context.MaintenanceRequests
                    .Where(r => r.unit_id == unitId)
                    .OrderByDescending(r => r.request_date)
                    .Join(_context.FinancialConstants,
                        request => request.service_id,
                        constant => constant.service_id,
                        (request, constant) => new
                        {
                            RequestId = request.request_id,
                            RawDate = request.request_date.ToString("yyyy-MM-ddTHH:mm:ss"),
                            Type = constant.service_name,
                            Status = request.request_status,
                            Price = constant.service_price,
                            Feedback = request.feedback
                        })
                    .ToListAsync();

                return Ok(requests);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في استرجاع سجل طلبات الصيانة", detail = ex.Message });
            }
        }

        /// <summary>إنشاء طلب صيانة جديد</summary>
        [HttpPost("CreateRequest")]
        public async Task<IActionResult> CreateRequest([FromBody] MaintenanceRequestDto data)
        {
            if (data == null)
                return BadRequest(new { message = "بيانات الطلب غير مكتملة" });

            try
            {
                var unitExists = await _context.HousingUnits.AnyAsync(u => u.unit_id == data.UnitId);
                if (!unitExists)
                    return BadRequest(new { message = $"رقم الوحدة {data.UnitId} غير موجود" });

                var serviceExists = await _context.FinancialConstants.AnyAsync(s => s.service_id == data.ServiceId);
                if (!serviceExists)
                    return BadRequest(new { message = "نوع الخدمة المختارة غير متوفر" });

                var newRequest = new MaintenanceRequest
                {
                    unit_id = data.UnitId,
                    service_id = data.ServiceId,
                    request_date = DateTime.Now,
                    request_status = "قيد الانتظار",
                    feedback = ""
                };

                _context.MaintenanceRequests.Add(newRequest);
                await _context.SaveChangesAsync();

                return Ok(new { status = "Success", message = "تم إرسال طلب الصيانة بنجاح" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "فشل في حفظ الطلب", detail = ex.Message });
            }
        }

        /// <summary>
        /// يستقبل اختيار الساكن ويحدّث request_status ويحفظ feedback النصي
        /// </summary>
        [HttpPost("SetUserFeedback")]
        public async Task<IActionResult> SetUserFeedback([FromBody] UserFeedbackDto data)
        {
            if (data == null)
                return BadRequest(new { message = "البيانات غير مكتملة" });

            // التحقق من أن القيمة ضمن الخيارات المسموحة فقط
            var allowed = new[] { "تم تنفيذ الطلب", "قيد التنفيذ", "لم يتم تنفيذه" };
            if (!allowed.Contains(data.RequestStatus))
                return BadRequest(new { message = "حالة الطلب غير صالحة" });

            try
            {
                var request = await _context.MaintenanceRequests
                    .FirstOrDefaultAsync(r => r.request_id == data.RequestId);

                if (request == null)
                    return NotFound(new { message = "الطلب غير موجود" });

                // تحديث حالة الطلب باختيار الساكن
                request.request_status = data.RequestStatus;

                // حفظ التعليق النصي فقط عند "تم تنفيذ الطلب" أو "لم يتم تنفيذه"
                if (data.RequestStatus == "تم تنفيذ الطلب" || data.RequestStatus == "لم يتم تنفيذه")
                    request.feedback = data.Feedback ?? "";

                await _context.SaveChangesAsync();

                return Ok(new { status = "Success", message = "تم حفظ تقييمك بنجاح" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "فشل في حفظ التقييم", detail = ex.Message });
            }
        }

        // --- 2. قسم المدفوعات والفواتير ---

        [HttpGet("GetPayments/{unitId}")]
        public async Task<IActionResult> GetPayments(int unitId)
        {
            try
            {
                var payments = await _context.FinancialPayments
                    .Where(p => p.unit_id == unitId)
                    .OrderByDescending(p => p.payment_date)
                    .Join(_context.FinancialConstants,
                        payment => payment.service_id,
                        constant => constant.service_id,
                        (payment, constant) => new
                        {
                            payment_id = payment.payment_id,
                            total_service_fee = payment.total_service_fee,
                            payment_date = payment.payment_date.ToString("yyyy/MM/dd"),
                            payment_method = payment.payment_method,
                            service_name = constant.service_name
                        })
                    .ToListAsync();

                return Ok(payments);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "خطأ في جلب السجل المالي", detail = ex.Message });
            }
        }
    }

    // ─── DTOs ────────────────────────────────────────────────────────────────

    public class MaintenanceRequestDto
    {
        [JsonPropertyName("unitId")]
        public int UnitId { get; set; }

        [JsonPropertyName("serviceId")]
        public int ServiceId { get; set; }

        [JsonPropertyName("preferredDate")]
        public DateTime PreferredDate { get; set; }
    }

    public class UserFeedbackDto
    {
        [JsonPropertyName("requestId")]
        public int RequestId { get; set; }

        /// <summary>"تم تنفيذ الطلب" | "قيد التنفيذ" | "لم يتم تنفيذه"</summary>
        [JsonPropertyName("requestStatus")]
        public string RequestStatus { get; set; }

        /// <summary>تعليق نصي — يُرسَل مع "تم تنفيذ الطلب" أو "لم يتم تنفيذه" فقط</summary>
        [JsonPropertyName("feedback")]
        public string? Feedback { get; set; }
    }
}