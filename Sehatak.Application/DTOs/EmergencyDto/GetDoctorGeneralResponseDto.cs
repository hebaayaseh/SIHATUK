using Sehatak.Domain.Enums;

namespace Sehatak.Application.DTOs.EmergencyDto
{
    public class GetDoctorGeneralResponseDto
    {
        public int DoctorUserId { get; set; }
        public string DoctorName { get; set; }
        public ShiftGroup ShiftName {  get; set; }
        public string? PhoneNumber { get; set; }
        public string Email { get; set; }
        public AttendanceStatus AttendanceStatus { get; set; }
        public DateOnly Date { get; set; }
    }
}
