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
        Task<MedicalRecordResponseDto> AddMedicalRecordAsync(int centerId,int userId, MedicalReqordRequestDto request);
        Task<string> EditMedicalRecordAsync(int centerId , int userId, UpdateMedicalRecordRequestDto request);
    }
}
