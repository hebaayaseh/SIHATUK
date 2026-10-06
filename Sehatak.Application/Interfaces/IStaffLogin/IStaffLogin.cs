using Sehatak.Application.DTOs.StaffLogIn;

namespace Sehatak.Application.Interfaces.StaffLogin
{
    public interface IStaffLogin
    {
        Task<StaffLoginResponseDto> StaffLoginAsync(int centerId, StaffLoginRequestDto request, string? ipAddress);
    }
}
