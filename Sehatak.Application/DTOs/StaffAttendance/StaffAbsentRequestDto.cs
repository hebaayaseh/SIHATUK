using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.StaffAttendance
{
    public class StaffAbsentRequestDto
    {
        public int userId {  get; set; }
        public DateOnly AttendanceDate {  get; set; }
    }
}
