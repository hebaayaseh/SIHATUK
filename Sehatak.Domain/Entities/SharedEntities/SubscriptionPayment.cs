using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Domain.Entities.SharedEntities
{
    public class SubscriptionPayment
    {
        public int Id { get; set; }
        public int? CenterId { get; set; }
        public int? SubscriptionId { get; set; }

        public int? RequestId { get; set; }

        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string? ReferenceNumber { get; set; }
        public string? ReceiptImageUrl { get; set; }
        public DateTime PaidAt { get; set; } = DateTime.UtcNow;
        public int? RecordedBySuperAdminId { get; set; }
        public string? Notes { get; set; }
        // Navigation Probarity : 
        public SuperAdmin? RecordedBy { get; set; }
        public MedicalCenter? Center { get; set; }
        public CenterSubscription? Subscription { get; set; }
        public CenterRegistrationRequest? Request { get; set; }
    }
}
