using Sehatak.Domain.Enums.PaymentEnums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.PaymentDto
{
    public class PaymentRequestDto
    {
        public int PatientId { get; set; }
        public decimal Amount { get; set; }
        public PaymentMethod Method { get; set; } = PaymentMethod.online;
        public PaymentType Type { get; set; } 
        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
        public DateTime PaidAt { get; set; } = DateTime.UtcNow;
        public int? ConsultationId { get; set; }
    }
}
