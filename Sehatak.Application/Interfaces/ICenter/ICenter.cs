using Sehatak.Application.DTOs.CentersDto;
using Sehatak.Application.DTOs.CreateCenterRequestDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.MedicalCenter
{
    public interface ICenter
    {
        Task<CenterResponseDto> CreateCenterAsync(createCenterRequestDto request);
        Task<CreateAdminResponseDto> CreateAdminAsync(int centerId, CreateAdminRequestDto request);
        Task<SpasificCenterResponseDto> GetSpasificCenterById(int centerId);
        Task<List<ListOfCentersResponse>> GetListOfCenters();
        Task<bool> SuspendedCenter(int centerId);
        Task<bool> ActiveCenter(int centerId);
    }
}
