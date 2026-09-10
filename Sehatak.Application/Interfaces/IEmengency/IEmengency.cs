using Sehatak.Application.Common;
using Sehatak.Application.DTOs.EmergencyDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.IEmengency
{
    public interface IEmengency
    {
        Task<EmergencyResponseDto> RegisterPatientEmergencyAsync(int centerId, int userId, EmergencyRequestDto request);
        Task<EmergencyResponseDto> EditPatientEmergencyAsync(int centerId, int userId, int emergencyId);
        Task<PagedResult<EmergencyResponseDto>> GetPatientEmergencyAsync(int centerId, PagedRequest request);
    }
}
