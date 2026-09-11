

using Sehatak.Application.DTOs.ViewProfileDto;

namespace Sehatak.Application.Interfaces.IViewProfile
{
    public interface IViewProfile
    {
        Task<ViewPatientProfileResponseDto> ViewPatientProfileAsync(int centerId, int userId);
        Task<string> PatientDeactiveProfileAsync(int centerId, int userId); 
    }
}
