using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.PaymentDto
{
    public class PaymentResponseDto
    {
        public int Id { get; set; }
        public int patientId {  get; set; }
        public string? ReferenceNumber { get; set; }
        public string? ReceiptImageUrl { get; set; }
        public DateTime PaidAt { get; set; } = DateTime.UtcNow;
        public int? RecordedBySuperAdminId { get; set; }
        public string? Notes { get; set; }
    }
}
