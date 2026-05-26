using StarComplexAPI.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("resident_reports")]
public class ResidentReport
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Display(Name = "رقم البلاغ")]
    public int report_id { get; set; }

    [Required]
    [ForeignKey("Resident")]
    [Display(Name = "رقم الساكن")]
    public int resident_id { get; set; }

    [Required]
    [StringLength(255)]
    [Display(Name = "عنوان البلاغ")]
    public string? title { get; set; }

    [Column(TypeName = "TEXT")]
    [Display(Name = "وصف المشكلة")]
    public string? description { get; set; }

    [StringLength(50)]
    [Display(Name = "تصنيف البلاغ")]
    public string? category { get; set; }

    // القيم المتاحة: عالي | متوسط | عادي
    [Required]
    [StringLength(20)]
    [Display(Name = "الأولوية")]
    public string priority { get; set; } = "متوسط";

    // القيم المتاحة: قيد الانتظار | جاري المعالجة | تم الحل | مرفوض
    [Required]
    [StringLength(50)]
    [Display(Name = "حالة البلاغ")]
    public string status { get; set; } = "قيد الانتظار";

    [StringLength(500)]
    [Display(Name = "مرفق الصورة")]
    public string? attachment_path { get; set; }

    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Display(Name = "تاريخ الإنشاء")]
    public DateTime created_at { get; set; } = DateTime.Now;

    // تم حذف [Timestamp] RowVersion — غير متوافق مع MySQL
}