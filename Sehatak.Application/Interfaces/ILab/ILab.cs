

using Sehatak.Application.Common;
using Sehatak.Application.DTOs.LabDto;

namespace Sehatak.Application.Interfaces.ILab
{
    public interface ILab
    {
        Task<LabRequestResponseDto> CreateLabRequestAsync(int centerId,int userId, CreateLabRequestDto request);
        Task<LabRequestResponseDto> UpdateLabRequestAsync(int centerId, int userId, UpdateLabRequestDto request);
        Task<PagedResult<LabRequestResponseDto>> GetLabRequestForPatientAsync(int centerId, int userId, int patientId,PagedRequest request);
        Task<PagedResult<PatientGetLabRequestReponseDto>> PatientGetLabRequestAsync(int centerId, int userId , PagedRequest request, int?subPatientId);
    }
}
