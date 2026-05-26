using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Linq;

namespace StarComplexAPI.Models
{
    [Table("residents")]
    public class Resident
    {
        [Key]
        public int resident_id { get; set; }

        public int unit_id { get; set; }

        public string? resident_code { get; set; }

        public string? first_name { get; set; }
        public string? second_name { get; set; }
        public string? third_name { get; set; }

        public string? phone_number { get; set; }

        public string? resident_type { get; set; }

        // صور العقد (أمام وخلف)
        public string? contract_path_1 { get; set; }
        public string? contract_path_2 { get; set; }

        public int? family_members_count { get; set; }

        // الحقول الناقصة المضافة من الصورة
        public string? name_hash { get; set; }
        public int? login_count { get; set; }
        public DateTime? last_login { get; set; }

        // الربط مع جدول الوحدات السكنية
        [ForeignKey("unit_id")]
        [JsonIgnore]
        public virtual HousingUnit? HousingUnit { get; set; }

        // الربط مع جدول أفراد العائلة
        public virtual ICollection<FamilyMember> FamilyMembers { get; set; } = new List<FamilyMember>();

        // خاصية محسوبة للاسم الكامل (غير مخزنة في قاعدة البيانات)
        [NotMapped]
        public string FullName => string.Join(" ",
            new[] { first_name, second_name, third_name }
            .Where(n => !string.IsNullOrWhiteSpace(n)));
    }
}