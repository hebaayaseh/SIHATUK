using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.DTOs.ShiftDto
{
    public class UpdateShiftSchedualRequestDto
    {
        public int ShiftId { get; set; }
        public ShiftGroup? ShiftName { get; set; }
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
    }
}
