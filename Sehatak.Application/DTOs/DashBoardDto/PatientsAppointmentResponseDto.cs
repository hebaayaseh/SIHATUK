

namespace Sehatak.Application.DTOs.DashBoardDto
{
    public class PatientsAppointmentResponseDto
    {
        public int AppointmentId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public int DoctorId { get; set; }
        public string DoctorName { get; set; }
        public TimeOnly? TimeSlot { get; set; }
        public DateTime? CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }
        public DateTime? ActualStart { get; set; }
        public DateTime? ActualEnd { get; set; }
        public bool IsFollowUp { get; set; }
        public string AppointmentStatus { get; set; }
    }
}
