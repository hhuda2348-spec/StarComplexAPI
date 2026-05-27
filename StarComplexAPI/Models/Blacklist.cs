using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
[Table("blacklist")]
public class Blacklist
{
    [Key]
    public int blacklist_id { get; set; }

    [StringLength(255)]
    [Required]
    public string? person_name { get; set; }

    [Required]
    public int employee_id { get; set; }

    [DataType(DataType.DateTime)]
    public DateTime added_date { get; set; } = DateTime.Now;

    [StringLength(500)]
    public string? reason { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
