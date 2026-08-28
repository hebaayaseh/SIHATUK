using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.ShiftDto
{
    public class GetStaffsShitfResponseDto
    {
        public int userId { get; set; }
        public string role { get; set; }
        public string email { get; set; }
        public string? phoneNumber { get; set; }
        public string name { get; set; }
        public bool isActive { get; set; }
        public bool userIsActive {  get; set; }
        public string address { get; set; }
        public string city { get; set; }
        public AttendanceStatus status {  get; set; }

    }
}
