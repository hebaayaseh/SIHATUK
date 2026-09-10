

namespace Sehatak.Application.DTOs.DoctorDto
{
    public class AppointmentSummaryDto
    {
        public int appointmentId {  get; set; }
        public int patientId { get; set; }
        public string patientName { get; set; }
        public DateOnly date {  get; set; }
        public TimeOnly timeSlot {  get; set; }
        public bool IsfollowUp { get; set; }

    }
}
