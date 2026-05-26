using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("visits", Schema = "star_complex")]
public class Visit
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int? visit_id { get; set; }

    [Required]
    public int? unit_id { get; set; }

    [Required]
    [StringLength(255)]
    public string? visitor_name { get; set; }

    [StringLength(100)]
    public string? visitor_type { get; set; }

    [StringLength(50)]
    public string? car_number { get; set; }

    // ── حقول خط نقل الطلاب ──
    [StringLength(50)]
    public string? selected_month { get; set; }

    public string? selected_days { get; set; }

    [StringLength(50)]
    public string? morning_window { get; set; }

    [StringLength(50)]
    public string? afternoon_window { get; set; }
    // ──────────────────────

    [DataType(DataType.Date)]
    public DateTime? visit_date { get; set; }

    [DataType(DataType.DateTime)]
    public DateTime? expiry_date { get; set; }

    [StringLength(20)]
    public string? visit_status { get; set; } = "مقبولة";

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}