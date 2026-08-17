using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.GetStaffDto
{
    public class SummaryShiftDto
    {
        public DateOnly ShistDate { get; set; }
        public ShiftGroup ShiftName { get; set; }

        public bool IsActive { get; set; } = true;
        public DateOnly AttendanceDate { get; set; }

        public DateTime? CheckInTime { get; set; }

        public DateTime? CheckOutTime { get; set; }

        public AttendanceStatus attendanceStatus { get; set; } 
    }
}
