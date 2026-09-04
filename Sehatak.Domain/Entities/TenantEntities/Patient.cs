using Sehatak.Domain.Enums;

namespace Sehatak.Domain.Entities.TenantEntities
{
    public class Patient
    {
        public int patientId {  get; set; }
        public int? userId {  get; set; }
        //If the patient is the main one
        public int? ParentPatientId { get; set; }
        public string? FirstName { get; set; }         
        public string? LastName { get; set; }
        public DateOnly DateOfBith { get; set; }
        public string WhatsappNumber { get; set; }
        public BloodType BloodType { get; set; }
        public Gender Gender { get; set; }

        // Navigation Properties : 

        public User user { get; set; }
        public Patient? ParentPatient { get; set; }
        public ICollection<Patient> SubPatients { get; set; } = new List<Patient>();
        public ICollection<Appointment> appointments { get; set; }=new List<Appointment>();
        public ICollection<Payment> payments { get; set; } = new List<Payment>();
        public ICollection<Waitlist> Waitlists { get; set; } = new List<Waitlist>();
        public ICollection<LabResult> LabResults { get; set; } = new List<LabResult>();
        public ICollection<Consultation> Consultations { get; set; } = new List<Consultation>();
        public ICollection<DoctorRating> Ratings { get; set; } = new List<DoctorRating>();
        public ICollection<MedicalRecord> MedicalRecords { get; set; } = new List<MedicalRecord>();

    }
}
