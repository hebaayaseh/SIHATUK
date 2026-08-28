using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.GetStaffDto
{
    public class SummarySchedualDto
    {
        public int Id { get; set; }
        public DayOfWeek DayOfWeek { get; set; }

        public TimeOnly StartTime { get; set; }

        public TimeOnly EndTime { get; set; }
        public bool IsActive { get; set; } = true;
        public int? SlotDurationMinutes { get; set; }
    }
}
