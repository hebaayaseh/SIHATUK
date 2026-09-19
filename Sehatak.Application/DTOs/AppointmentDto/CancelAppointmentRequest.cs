
namespace Sehatak.Application.DTOs.AppointmentDto
{
    public class CancelAppointmentRequest
    {
        public TimeOnly timeSlot {  get; set; }
        public DateOnly date {  get; set; }
        public string? Resone {  get; set; }
        public int? SubPatientId { get; set; }

    }
}
