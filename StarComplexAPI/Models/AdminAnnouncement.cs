using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("admin_announcements")]
public class AdminAnnouncement
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Display(Name = "رقم التعميم")]
    public int announcement_id { get; set; }

    [Required]
    [Display(Name = "رقم المسؤول")]
    public int admin_id { get; set; }

    /// <summary>
    /// مطابقة الـ ENUM العربي بدقة مع الكويري
    /// </summary>
    [Required]
    [Column(TypeName = "ENUM('جميع الوحدات السكنية', 'الوحدات المؤجرة', 'وحدة سكنية مفردة')")]
    [RegularExpression(@"^(جميع الوحدات السكنية|الوحدات المؤجرة|وحدة سكنية مفردة)$",
        ErrorMessage = "يجب اختيار فئة مستهدفة صحيحة")]
    [Display(Name = "الفئة المستهدفة")]
    public string target_type { get; set; } = "جميع الوحدات السكنية";

    [Display(Name = "رقم الوحدة")]
    public int? unit_id { get; set; } // يبقى Null إذا لم تكن الوحدة مفردة

    [Required]
    [StringLength(255)]
    [Display(Name = "العنوان")]
    public string? title { get; set; }

    [Required]
    [Column(TypeName = "TEXT")]
    [Display(Name = "محتوى التعميم")]
    public string? message_content { get; set; }

    [Required]
    [Column(TypeName = "TINYINT(1)")] // لضمان المطابقة مع BOOLEAN في MySQL
    [Display(Name = "يتطلب إجراء؟")]
    public bool action_required { get; set; } = false;

    [StringLength(255)]
    [Display(Name = "وصف الإجراء")]
    public string? action_description { get; set; }

    [Column(TypeName = "DATETIME")] // تحديد النوع بدقة ليتطابق مع الكويري
    [Display(Name = "تاريخ الاستحقاق")]
    public DateTime? due_date { get; set; }

    [Required]
    [Column(TypeName = "TINYINT(1)")]
    [Display(Name = "عاجل")]
    public bool is_urgent { get; set; } = false;

    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column(TypeName = "TIMESTAMP")]
    [Display(Name = "تاريخ النشر")]
    public DateTime created_at { get; set; } = DateTime.Now;

    [Timestamp]
    [Column(TypeName = "TIMESTAMP")]
    [Display(Name = "إصدار البيانات")]
    public byte[]? RowVersion { get; set; }

    // الربط البرمجي (Navigation Property)
    [ForeignKey("admin_id")]
    public virtual Employee? Admin { get; set; }
}