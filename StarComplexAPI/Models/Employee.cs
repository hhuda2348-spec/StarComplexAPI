using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("employees")]
public class Employee
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int employee_id { get; set; }

    [StringLength(20)]
    [Required]
    public string? employee_code { get; set; }

    [StringLength(50)]
    public string? first_name { get; set; }

    [StringLength(50)]
    public string? second_name { get; set; }

    [StringLength(50)]
    public string? third_name { get; set; }

    [StringLength(100)]
    public string? job_title { get; set; }

    [StringLength(20)]
    [Phone]
    public string? phone_number { get; set; }

    [StringLength(255)]
    public string? national_id_front_path { get; set; }

    [StringLength(255)]
    public string? national_id_back_path { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    [StringLength(20)]
    public string? employee_type { get; set; }

    [StringLength(255)]
    public string? employee_index { get; set; }

    // ══════════════════════════════════════════════════════════════
    // password_hash — الهاش الرئيسي للتحقق
    // المدخل: Argon2id("fullName||code")
    // ══════════════════════════════════════════════════════════════
    [StringLength(512)]
    public string? password_hash { get; set; }

    // ══════════════════════════════════════════════════════════════
    // name_hash — تشفير الاسم لكلا النوعين (security + admin)
    // يُولَّد في أول دخول ناجح — يُستخدم للتحقق من الاسم لاحقاً
    // ══════════════════════════════════════════════════════════════
    [StringLength(512)]
    public string? name_hash { get; set; }

    // ❌ name_hash_admin — محذوف، name_hash يغطي الكلا النوعين

    public int? login_count { get; set; }

    public DateTime? last_login { get; set; }
}