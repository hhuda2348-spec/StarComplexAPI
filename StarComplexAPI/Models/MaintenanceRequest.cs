using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StarComplexAPI.Models
{
    [Table("maintenance_requests")]
    public class MaintenanceRequest
    {
        [Key]
        public int request_id { get; set; }

        public int unit_id { get; set; }

        public int service_id { get; set; }

        public DateTime request_date { get; set; } = DateTime.Now;

        /// <summary>
        /// حالات الطلب:
        /// "قيد الانتظار"    - الطلب الجديد (من النظام)
        /// "قيد التنفيذ"     - اختيار الساكن
        /// "تم تنفيذ الطلب"  - اختيار الساكن
        /// "لم يتم تنفيذه"  - اختيار الساكن
        /// </summary>
        public string? request_status { get; set; } = "قيد الانتظار";

        /// <summary>تعليق الساكن النصي فقط</summary>
        public string? feedback { get; set; }
    }
}