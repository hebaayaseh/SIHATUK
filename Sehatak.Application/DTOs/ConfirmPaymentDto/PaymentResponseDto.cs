

namespace Sehatak.Application.DTOs.ConfirmPaymentDto
{
    public class PaymentResponseDto
    {
        public int PaymentId { get; set; }
        public decimal Amount { get; set; }
        public string? Status { get; set; }
        public string? Method { get; set; }
        public DateTime PaidAt {  get; set; }
    }
}
