using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StarComplexAPI.Models
{
    [Table("employees_archive")]
    public class EmployeeArchive
    {
        [Key]
        [Column("archive_id")]
        public int archive_id { get; set; }

        // ✅ مطابق للكنترولر: emp.employee_id
        [Column("employee_id")]
        public int employee_id { get; set; }

        [Column("first_name")]
        [StringLength(100)]
        public string? first_name { get; set; }

        [Column("second_name")]
        [StringLength(100)]
        public string? second_name { get; set; }

        [Column("third_name")]
        [StringLength(100)]
        public string? third_name { get; set; }

        [Column("job_title")]
        [StringLength(100)]
        public string? job_title { get; set; }

        [Column("phone_number")]
        [StringLength(20)]
        public string? phone_number { get; set; }

        // ✅ مطابق للكنترولر: archive.archived_at
        [Column("archived_at")]
        public DateTime archived_at { get; set; } = DateTime.Now;

        // للقراءة فقط — لا يُخزَّن في قاعدة البيانات
        [NotMapped]
        public string FullName =>
            $"{first_name} {second_name} {third_name}".Trim();
    }
}