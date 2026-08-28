using Sehatak.Domain.Enums;

namespace Sehatak.Application.DTOs.ShiftDto
{
    public class DailyAttendanceDto
    {
        public DateOnly Date { get; set; }
        public AttendanceStatus? Status { get; set; }
        public bool isActive { get; set; }
    }
}