using Sehatak.Application.DTOs.ShiftDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.IShiftSchedule
{
    public interface IShift
    {
        Task<ShiftScheduleResponse> AddShiftSchedule(int userId,int centerId, ShiftScheduleRequest request);
        Task<string> AssignShiftToStaffAsync(int userId, int centerId, AssignShiftToStaffRequestDto request);
    }
}
