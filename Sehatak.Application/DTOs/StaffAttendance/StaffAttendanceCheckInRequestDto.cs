using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.StaffAttendance
{
    public class StaffAttendanceCheckInRequestDto
    {
        public int UserId { get; set; }
        public int StaffShiftId { get; set; }

        public DateOnly AttendanceDate { get; set; }

        public DateTime CheckTime { get; set; }

    }
}
