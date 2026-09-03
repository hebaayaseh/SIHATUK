using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.FeatureCenterDto;
using Sehatak.Application.DTOs.FeatureDto;
using Sehatak.Application.Interfaces.Features;
using Sehatak.Domain.Entities.SharedEntities;

namespace Sehatak.API.Controllers.SuperAdminController.FeatureOperation
{
    [ApiController]
    [Route("[Controller]")]
    public class FeactureController : ControllerBase
    {
        private readonly IFeature featureService;
        public FeactureController(IFeature featureService)
        {
            this.featureService = featureService;
        }
        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("superAdmin-add_feature")]
        public async Task<IActionResult>AddFeature([FromBody] CreateFeatureRequestDto featureDto)
        {
            var result =await featureService.AddFeatureAsync(featureDto);
            
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("superAdmin-active-feature/{centerId}")]
        public async Task<IActionResult> ActiveFeature(int centerId, [FromBody] ActiveFetureRequest request)
        {
            var result = await featureService.ActiveFeaturAsync(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("add-feature-to-center/{centerId}")]
        public async Task<IActionResult> AddFeatureToCenter(int centerId, [FromBody] AddFeatureToCenterRequest request)
        {
            var result = await featureService.AddFeatureToCenterAsync(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpGet("superAdminOrAdmin-get-all-feature")]
        public async Task<IActionResult> GetAllFeature([FromQuery] PagedRequest request)
        {
            var result = await featureService.GetAllFeatureAsync(request);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpDelete("superAdmin-remove-feature-from-center/{centerId}")]
        public async Task<IActionResult> RemoveFeatureFromCenter(int centerId, [FromBody] RemoveFeatureFromCenterRequest featureId)
        {
            var result = await featureService.RemoveFeatureFromCenterAsync(centerId, featureId);
            return Ok(result);
        }
    }
}
