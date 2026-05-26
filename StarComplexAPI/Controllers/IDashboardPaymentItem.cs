namespace StarComplexAPI.Controllers
{
    public interface IDashboardPaymentItem
    {
        string PaymentDate { get; set; }
        string PaymentId { get; set; }
        string PaymentMethod { get; set; }
        string ServiceName { get; set; }
        string TotalFee { get; set; }
        string UnitId { get; set; }
    }
}