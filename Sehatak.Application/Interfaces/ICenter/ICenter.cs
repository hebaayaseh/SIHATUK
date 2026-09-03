using Sehatak.Application.Common;
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
        Task<PagedResult<ListOfCentersResponseDto>> GetListOfCenters(PagedRequest request);
        Task<bool> SuspendedCenter(int centerId);
        Task<bool> ActiveCenter(int centerId);
    }
}
