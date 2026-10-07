using Sehatak.Application.Common;
using Sehatak.Application.DTOs.MedicalRecordDto;

namespace Sehatak.Application.Interfaces.IMedicalRecord
{
    public interface IMedicalRecord
    {
        Task<MedicalRecordDetailResponseDto> AddMedicalRecordAsync(int centerId,int userId, MedicalRecordDetailRequestDto request);
        Task<MedicalRecordDetailResponseDto> EditMedicalRecordAsync(int centerId , int userId, UpdateMedicalRecordRequestDto request);
        Task<PagedResult<MedicalRecordDetailResponseDto>> GetPatientMedicalHistoryAsync(int centerId, int userId, int patientId, PagedRequest request);
        Task<MedicalRecordDetailResponseDto> GetMedicalRecordByIdAsync(int centerId, int userId, int medicalRecordId);
        Task<PagedResult<PatientGetMedicalHistoryResponseDto>> PatientgetMedicalRecordHistoryAsync(int centerId, int userId, PagedRequest request,int? subPatientId);
        

    }
}
