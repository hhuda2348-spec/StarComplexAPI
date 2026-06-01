// train/Models/MaintenanceRequest.cs
// موديل العميل — يحتوي على خصائص إضافية للعرض (ليست في قاعدة البيانات)
using System.Text.Json.Serialization;

namespace train.Models
{
    public class MaintenanceRequest
    {
        [JsonPropertyName("request_id")]
        public int request_id { get; set; }

        [JsonPropertyName("unit_id")]
        public int unit_id { get; set; }

        [JsonPropertyName("service_id")]
        public int service_id { get; set; }

        [JsonPropertyName("request_date")]
        public DateTime request_date { get; set; }

        [JsonPropertyName("request_status")]
        public string request_status { get; set; } = "قيد الانتظار";

        [JsonPropertyName("feedback")]
        public string feedback { get; set; } = string.Empty;

        // ── خصائص إضافية تأتي من الـ JOIN في الكنترولر ──────────────
        [JsonPropertyName("service_name")]
        public string service_name { get; set; } = string.Empty;

        [JsonPropertyName("service_price")]
        public decimal service_price { get; set; }

        [JsonPropertyName("unit_type")]
        public string? unit_type { get; set; }

        [JsonPropertyName("unit_status")]
        public string? unit_status { get; set; }

        // ── خاصية مشتقة للون الحالة (تُحسب محلياً) ──────────────────
        public string StatusColorHex => request_status switch
        {
            "قيد الانتظار" => "#D4A017",
            "قيد التنفيذ" => "#1a6e3c",
            "تم تنفيذ الطلب" => "#28a745",
            "لم يتم تنفيذه" => "#dc3545",
            _ => "#888888"
        };
    }
}