using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StarComplexAPI.Models
{
    [Table("financial_constants")]
    public class FinancialConstant
    {
        [Key]
        public int service_id { get; set; } // المعرف (1، 2، 3، 4، 5)

        public string service_name { get; set; }
        /* * قائمة الخدمات حسب قاعدة البيانات:
         * 1: صيانة الكهرباء
         * 2: صيانة الصحيات
         * 3: صيانة السبالت
         * 4: صيانة عامة
         * 5: الايجار الشهري
         */

        public decimal service_price { get; set; }
        /* * قائمة الأسعار حسب قاعدة البيانات:
         * صيانة الكهرباء -> 25000.00
         * صيانة الصحيات -> 15000.00
         * صيانة السبالت -> 30000.00
         * صيانة عامة    -> 10000.00
         * الايجار الشهري -> 500000.00
         */
    }
}