using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.GetStaffDto
{
    public class GetStaffResponseDto
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
        public List<SummaryShiftDto> StaffShift { get; set; }
    }
}
