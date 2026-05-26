using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StarComplexAPI.Models
{
    [Table("financial_payments")]
    public class FinancialPayment
    {
        [Key]
        public int payment_id { get; set; }
        public int unit_id { get; set; }
        public int service_id { get; set; }
        public decimal total_service_fee { get; set; }
        public int? employee_id { get; set; }
        public System.DateTime payment_date { get; set; } = DateTime.Now;
        public string payment_method { get; set; }// كاش او دفع الكتروني
       
 public decimal accont_received { get; set; }// 
    }
}