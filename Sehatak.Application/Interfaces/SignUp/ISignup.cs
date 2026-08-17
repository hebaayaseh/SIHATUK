using Sehatak.Application.DTOs.StaffSignup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.SignUp
{
    public interface ISignup
    {
        Task<AddStafResponseDto> AddStafAsync(int userId, int centerId, AddStaffRequestDto request);
    }
}
