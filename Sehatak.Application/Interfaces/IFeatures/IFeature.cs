using Sehatak.Application.DTOs.FeatureCenterDto;
using Sehatak.Application.DTOs.FeatureDto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sehatak.Application.Interfaces.Features
{
    public interface IFeature 
    {
         Task<FeatureResponseDto> AddFeatureAsync(CreateFeatureRequestDto requestDto);
        Task<bool> AddFeatureToCenterAsync(int centerId, AddFeatureToCenterRequest request);
        Task<bool> ActiveFeaturAsync(int centerId, ActiveFetureRequest request);
        Task<List<FeatureResponseDto>> GetAllFeatureAsync();
        Task<bool> RemoveFeatureFromCenterAsync(int centerId, RemoveFeatureFromCenterRequest request);
    }

}
