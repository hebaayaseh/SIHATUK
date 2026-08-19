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

    }
}
