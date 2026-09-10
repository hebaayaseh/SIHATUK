using Sehatak.Application.Common;
using Sehatak.Application.DTOs.EmergencyDto;

namespace Sehatak.Application.Interfaces.IEmerngency
{
    public interface IEmerngency
    {
        Task<EmergencyResponseDto> RegisterPatientEmergencyAsync(int centerId, int userId, EmergencyRequestDto request);
        Task<EmergencyResponseDto> EditPatientEmergencyAsync(int centerId, int userId, UpdateEmergencyRequestDto request);
        Task<PagedResult<EmergencyResponseDto>> GetPatientEmergencyAsync(int centerId, PagedRequest request);
        Task<PagedResult<GetDoctorGeneralResponseDto>> GetDoctorGeneralAsync(int centerId,PagedRequest request);
    }
}
