using DocumentFormat.OpenXml.Wordprocessing;
using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.ShiftDto
{
    public class AssignShiftToStaffRequestDto
    {
        public int UserId { get; set; }
        public ShiftGroup ShiftName { get; set; }
        public DateOnly ShiftDate { get; set; }
    }
}
