using Sehatak.Application.Common;
using Sehatak.Application.DTOs.ShiftDto;
using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.IShiftSchedule
{
    public interface IShift
    {
        Task<ShiftScheduleResponse> AddShiftSchedule(int centerId, ShiftScheduleRequest request);
        Task<string> AssignShiftToStaffAsync(int centerId , AssignShiftToStaffRequestDto request);
        Task<GetShiftsScheduleResponseDto> GetShiftsSchedulesAsync(int centerId);
        Task<ShiftScheduleResponse> UpdateShiftScheduleAsync(int centerId, UpdateShiftSchedualRequestDto request);
        Task<string> DeleteShiftSchedualeAsync(int centerId, int shiftId);
        Task<PagedResult<GetStaffsShitfResponseDto>> GetStaffsWithShiftAsync(int centerId, ShiftGroup shift,PagedRequest request, int? year = null, int? month = null );
        Task<PagedResult<StaffDirectoryResponseDto>> GetAllStaffAsync(int centerId,PagedRequest request);
    }
}
