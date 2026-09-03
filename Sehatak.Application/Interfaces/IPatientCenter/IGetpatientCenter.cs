using Sehatak.Application.Common;
using Sehatak.Application.DTOs.PatientCenter;
using Sehatak.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.IPatientCenter
{
    public interface IGetpatientCenter
    {
        Task<PagedResult<GetPatientResponseDto>> GetPatientesAsync(int centerId , AppointmentStatus status, PagedRequest request);
        Task<GetPatientResponseDto> GetPatientAsync(int centerId , GetPatientRequestDto request);
    }
}
