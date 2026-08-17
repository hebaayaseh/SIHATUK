using Sehatak.Application.DTOs.ShiftDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.IShiftSchedule
{
    public interface IShiftSchedule
    {
        Task<ShiftScheduleResponse> AddShiftSchedule(int userId,int centerId, ShiftScheduleRequest request);
    }
}
