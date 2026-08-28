using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.GetStaffDto
{
    public class DoctorSummaryDto
    {
        public int DoctorId { get; set; }
        public string DoctorName { get; set; } = string.Empty;
        public string email { get; set; }
        public string? phoneNumber { get; set; }
        public string? Bio {  get; set; }
        public string? Specialization { get; set; }
        public string? ProfileImageUrl { get; set; }
        public bool OnlineEnabled { get; set; }
        public List<DateOnly?> BlockedDates { get; set; }
        public List<SummarySchedualDto> doctorSchedule {  get; set; }
        
    }
}
