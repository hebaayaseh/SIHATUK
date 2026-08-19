using System.ComponentModel.DataAnnotations;

namespace Sehatak.Domain.Entities.TenantEntities
{
    public class MedicalRecord
    {
        
        public int Id { get; set; }

        public int PatientId { get; set; }

        public int DoctorId { get; set; }
        public int? ConsultationId { get; set; }
        public int? AppointmentId { get; set; }
 

        // If you add a pharmacy, the prescription must be complete. If you don't add one, you can use the AI-powered prescription generator.
        public string Prescription { get; set; }
        public string Notes { get; set; }
        public string? Diagnosis { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        // Navigation Properties :
        public Patient Patient { get; set; } = null!;
        public Doctor Doctor { get; set; } = null!;
        public Appointment? Appointment { get; set; }
        public Consultation? Consultation { get; set; }
    }
}
