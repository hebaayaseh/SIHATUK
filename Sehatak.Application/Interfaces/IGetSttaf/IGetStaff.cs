using Sehatak.Application.DTOs.GetStaffDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.GetSttafInterFace
{
    public interface IGetStaff
    {
        Task<List<GetDoctorsResponseDto>> GetDoctorsAsync(int centerId);
        Task<DoctorSummaryDto> GetDoctorAsync(int centerId, int doctorId,int? year = null, int? month = null);
        
        
    }
}
