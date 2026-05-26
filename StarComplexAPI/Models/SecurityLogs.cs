using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StarComplexAPI.Models
{
    /// <summary>
    /// سجل بلاغات الأمن — كل إجراء أو بلاغ يقوم به موظف الأمن يُخزَّن هنا
    /// </summary>
    [Table("security_logs", Schema = "star_complex")]
    public class SecurityLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int log_id { get; set; }

        /// <summary>معرف الموظف الذي أجرى الإجراء</summary>
        [Required]
        public int employee_id { get; set; }

        /// <summary>معرف التصريح المرتبط بالبلاغ (اختياري)</summary>
        public int? visit_id { get; set; }

        /// <summary>
        /// نوع الإجراء:
        /// SCAN      — فحص تصريح
        /// ENTRY     — تسجيل دخول
        /// EXIT      — تسجيل خروج
        /// REPORT    — بلاغ يدوي من الموظف
        /// EMERGENCY — تفعيل طوارئ
        /// BLACKLIST_HIT — محاولة دخول شخص محظور
        /// </summary>
        [Required]
        [StringLength(30)]
        public string action_type { get; set; } = string.Empty;

        /// <summary>نتيجة الإجراء: APPROVED / REJECTED / INFO / WARNING</summary>
        [StringLength(20)]
        public string action_result { get; set; } = "INFO";

        /// <summary>تفاصيل البلاغ أو الملاحظة</summary>
        [StringLength(1000)]
        public string? notes { get; set; }

        /// <summary>اسم الزائر وقت الإجراء (لحفظ السجل حتى بعد حذف التصريح)</summary>
        [StringLength(255)]
        public string? visitor_snapshot { get; set; }

        /// <summary>رقم الوحدة وقت الإجراء</summary>
        public int? unit_id_snapshot { get; set; }

        [DataType(DataType.DateTime)]
        public DateTime created_at { get; set; } = DateTime.Now;

        // Navigation
        [ForeignKey(nameof(employee_id))]
        public virtual Employee? Employee { get; set; }
    }
}