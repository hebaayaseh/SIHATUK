using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.FeatureCenterDto;
using Sehatak.Application.Interfaces.AddFeatureToCenter;

namespace Sehatak.API.Controllers.SuperAdminController.FeatureOperation
{
    [ApiController]
    [Route("api/SuperAdminOnly")]
    public class AddFeatureToCenterController : ControllerBase
    {
        private readonly IAddFeatureToCenter addFeatureToCenterService;
        public AddFeatureToCenterController(IAddFeatureToCenter addFeatureToCenterService)
        {
            this.addFeatureToCenterService = addFeatureToCenterService;
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("add-feature-to-center/{centerId}")]
        public async Task<IActionResult> AddFeatureToCenter(int centerId, [FromBody] AddFeatureToCenterRequest request)
        {
            var result = await addFeatureToCenterService.AddFeatureToCenterAsync(centerId, request);
            return Ok(result);
        }
    }
}
