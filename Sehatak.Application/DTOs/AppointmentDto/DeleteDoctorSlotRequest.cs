

namespace Sehatak.Application.DTOs.AppointmentDto
{
    public class DeleteDoctorSlotRequest
    {
        public TimeOnly timeSlot {  get; set; }
        public DateOnly date { get; set; }
        public string? Reason {  get; set; }
    }
}
