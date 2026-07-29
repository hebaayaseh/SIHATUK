using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.RecordPaymentRequestDto
{
    public class PaymentResponseDto
    {
        public int Id { get; set; }
        public int? CenterId { get; set; }
        public string CenterName { get; set; } = string.Empty;
        public int? SubscriptionId { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string? ReferenceNumber { get; set; }
        public string? ReceiptImageUrl { get; set; }
        public DateTime PaidAt { get; set; }
        public string? RecordedBy { get; set; }
        public string? Notes { get; set; }
        public bool IsConfirmed { get; set; }
    }
}
