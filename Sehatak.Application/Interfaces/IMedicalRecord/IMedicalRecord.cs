using Sehatak.Application.Common;
using Sehatak.Application.DTOs.MedicalRecordDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.IMedicalRecord
{
    public interface IMedicalRecord
    {
        Task<MedicalRecordDetailResponseDto> AddMedicalRecordAsync(int centerId,int userId, MedicalRecordDetailRequestDto request);
        Task<MedicalRecordDetailResponseDto> EditMedicalRecordAsync(int centerId , int userId, UpdateMedicalRecordRequestDto request);
        Task<PagedResult<MedicalRecordDetailResponseDto>> GetPatientMedicalHistoryAsync(int centerId, int userId, int patientId, PagedRequest request);
        Task<MedicalRecordDetailResponseDto> GetMedicalRecordByIdAsync(int centerId, int userId, int medicalRecordId);

    }
}
