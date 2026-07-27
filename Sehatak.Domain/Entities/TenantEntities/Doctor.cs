using System.ComponentModel.DataAnnotations;

namespace Sehatak.Domain.Entities.TenantEntities
{
    public class Doctor
    {
        public int Id { get; set; }
        public int userId { get; set; }
        public int departmentId { get; set; }
        public string Specialization { get; set; }
        public string Bio { get; set; }
        
        // initial value = false 
        public bool OnlineEnabled { get; set; } = false;
        // For connect with service price table to get the consultation cost for the doctor
        public int? ConsultationCostId { get; set; }


        // Navigation Properties :
        public User user { get; set; }
        public Department department { get; set; }
        public ICollection<Appointment> dailyAppointment { get; set; } = new List<Appointment>();
        public ICollection<Waitlist> Waitlists { get; set; } = new List<Waitlist>();
        public ICollection<Doctorschedule> doctorschedules { get; set; } = new List<Doctorschedule>();
        public ICollection<DoctorRating> doctorRatings { get; set; } = new List<DoctorRating>();
        public ICollection<MedicalRecord> medicicalRecord { get; set; }=new List<MedicalRecord>();
        public ICollection<Consultation> Consultations { get; set; } = new List<Consultation>();
        public ServicePrice? ConsultationCost { get; set; }


    }
}
