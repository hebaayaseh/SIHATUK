using Sehatak.Application.DTOs.SubPatientDto;

namespace Sehatak.Application.Interfaces.ISubPatient
{
    public interface ISubPatient
    {
        Task<List<SummarySubPatientResponseDto>> AddSubPatientAsync(int centerId , int usertId ,AddSubPatientRequestDto request);
        Task<SummarySubPatientResponseDto> UpdateSubPatientAsync(int centerId, int userId,int subPatientId , UpdateSubPatientRequestDto request);
    }
}
