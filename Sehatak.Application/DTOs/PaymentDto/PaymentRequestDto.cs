using Microsoft.AspNetCore.Http;
using Sehatak.Domain.Enums.PaymentEnums;

namespace Sehatak.Application.DTOs.PaymentDto
{
    public class PaymentRequestDto
    {
        public PaymentMethod Method { get; set; } = PaymentMethod.online;
        public PaymentType Type { get; set; } 
        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
        public string? ReferenceNumber { get; set; }
        public IFormFile? ReceiptImageUrl { get; set; }
        public string? Notes { get; set; }

    }
}
