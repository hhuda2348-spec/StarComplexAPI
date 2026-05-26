using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StarComplexAPI.Models
{
    [Table("residents_archive")]
    public class ResidentArchive
    {
        [Key]
        public int archive_id { get; set; }

        public int unit_id { get; set; }

        public string? first_name { get; set; }
        public string? second_name { get; set; }
        public string? third_name { get; set; }

        public string? resident_type { get; set; }

        public string? phone_number { get; set; }

        public DateTime move_out_date { get; set; } = DateTime.Now;

        // مسارات صور العقد
        public string? contract_path_1 { get; set; }
        public string? contract_path_2 { get; set; }

        // مسارات صور الهوية الشخصية للساكن
        public string? national_id_front_path { get; set; }
        public string? national_id_back_path { get; set; }

        // خاصية محسوبة للاسم الكامل
        [NotMapped]
        public string FullName => string.Join(" ",
            new[] { first_name, second_name, third_name }
            .Where(n => !string.IsNullOrWhiteSpace(n)));
    }
}