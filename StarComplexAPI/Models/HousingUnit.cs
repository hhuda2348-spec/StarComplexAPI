using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StarComplexAPI.Models
{
    [Table("housing_units")]
    public class HousingUnit
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int unit_id { get; set; }

        public string? unit_type { get; set; }

        public string unit_status { get; set; } = "فارغ";

        public string? access_code { get; set; }

        // الحقل الناقص المضاف من الصورة
        public sbyte? is_hashed { get; set; }

        [JsonIgnore]
        public virtual ICollection<Resident> Residents { get; set; } = new List<Resident>();
    }
}