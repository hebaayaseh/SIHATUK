using Sehatak.Application.DTOs.StaffAttendance;
using Sehatak.Domain.Entities.TenantEntities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.IStaffAttendance
{
    public interface IStaffAttendance
    {
        Task<string> CheckInTimeAsync(int centerId, int userId, StaffAttendanceCheckInRequestDto request);
        Task<string> CheckOutTimeAsync(int centerId, int userId, StaffAttendanceCheckInRequestDto request);

    }
}
