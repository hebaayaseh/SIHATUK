

using Sehatak.Application.DTOs.ViewProfileDto;

namespace Sehatak.Application.Interfaces.IViewProfile
{
    public interface IViewProfile
    {
        Task<ViewPatientProfileResponseDto> ViewPatientProfileAsync(int centerId, int userId); 
        Task<ViewDoctorProfileResponseDto> ViewDoctorProfiAsync(int centerId, int userId);
        Task<ViewStaffProfileResponseDto> ViewStaffProfileAsync(int centerId, int userId);
    }
}
