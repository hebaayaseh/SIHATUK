using Sehatak.Application.DTOs.StaffLogIn;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.StaffLogin
{
    public interface IStaffLogin
    {
        Task<StaffLoginResponseDto> StaffLoginAsync(int centerId, StaffLoginRequestDto request);
    }
}
