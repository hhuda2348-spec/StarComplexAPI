using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace StarComplexAPI.Models
{
    [Table("family_members")]
    public class FamilyMember
    {
        [Key]
        public int member_id { get; set; }

        public int resident_id { get; set; }

        public string? first_name { get; set; }
        public string? second_name { get; set; }
        public string? third_name { get; set; }

        // مسارات صور الهويات (أمام وخلف)
        public string? national_id_front_path { get; set; }
        public string? national_id_back_path { get; set; }

        // الربط مع جدول الساكن الأساسي
        [ForeignKey("resident_id")]
        [JsonIgnore]
        public virtual Resident? Resident { get; set; }

        // خاصية محسوبة للاسم الكامل
        [NotMapped]
        public string FullName => string.Join(" ",
            new[] { first_name, second_name, third_name }
            .Where(n => !string.IsNullOrWhiteSpace(n)));
    } }

    