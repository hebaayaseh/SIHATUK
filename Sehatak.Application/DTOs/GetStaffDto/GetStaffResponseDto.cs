using Sehatak.Domain.Enums;
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
        public string Email { get; set; }
        public string Role { get; set; }
        public string? PhoneNumber { get; set; }
        public string Address { get; set; }
        public bool IsActive { get; set; }
        public List<SummaryShiftDto> StaffShift { get; set; }
    }
}
