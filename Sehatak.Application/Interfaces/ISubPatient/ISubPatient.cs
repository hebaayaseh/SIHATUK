using Sehatak.Application.Common;
using Sehatak.Application.DTOs.SubPatientDto;

namespace Sehatak.Application.Interfaces.ISubPatient
{
    public interface ISubPatient
    {
        Task<List<SummarySubPatientResponseDto>> AddSubPatientAsync(int centerId , int userId ,AddSubPatientRequestDto request);
        Task<SummarySubPatientResponseDto> UpdateSubPatientAsync(int centerId, int userId,int subPatientId , UpdateSubPatientRequestDto request);
        Task<PagedResult<SummarySubPatientResponseDto>> GetSubPatientsAsync(int centerId, int userId, PagedRequest request);
    }
}
