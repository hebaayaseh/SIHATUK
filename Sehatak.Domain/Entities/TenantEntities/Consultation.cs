using Sehatak.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace Sehatak.Domain.Entities.TenantEntities
{
    public class Consultation
    {
        
        public int Id { get; set; }

        public int PatientId { get; set; }

        public int DoctorId { get; set; }

        public DateTime? ScheduledAt { get; set; }

        public ConsultationStatus Status { get; set; } = ConsultationStatus.Pending;

        public string? VideoLink { get; set; }

        public string? Notes { get; set; }

        // Navigation Properties :
        public Patient Patient { get; set; } = null!;
        public Doctor Doctor { get; set; } = null!;
        public Payment? Payment { get; set; }
    }
}
