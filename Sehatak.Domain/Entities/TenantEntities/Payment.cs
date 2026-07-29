using Sehatak.Domain.Enums.PaymentEnums;

namespace Sehatak.Domain.Entities.TenantEntities
{
    public class Payment
    {
        public int Id { get; set; }
       
        public int PatientId { get; set; }
        public decimal Amount { get; set; }
        public PaymentMethod Method { get; set; }
        public PaymentType Type { get; set; }
        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
        public DateTime PaidAt { get; set; } = DateTime.UtcNow;
        public int? RecordedByStaffId { get; set; }
        // The paid must be linked to either an appointment, a consultation or a lab result :
        public int? LabResultId { get; set; }
        public int? ConsultationId { get; set; }
        public int? AppointmentId { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? ReceiptImageUrl { get; set; }
        public string? Notes { get; set; }

        // Navigation Properties : 
        public Appointment? Appointment { get; set; }
        public Patient Patient { get; set; } = null!;
        public User? Staff { get; set; }
        public LabResult? LabResult { get; set; }
        public Consultation? Consultation { get; set; }

    }
}
