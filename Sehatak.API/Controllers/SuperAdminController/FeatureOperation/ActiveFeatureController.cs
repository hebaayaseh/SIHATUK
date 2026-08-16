using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.FeatureCenterDto;
using Sehatak.Application.Interfaces.Features;

namespace Sehatak.API.Controllers.SuperAdminController.FeatureOperation
{
    [ApiController]
    [Route("api/SuperAdminOnly")]
    public class ActiveFeatureController : ControllerBase
    {
        private readonly IActiveFeature activeFeatureService;
        public ActiveFeatureController(IActiveFeature activeFeatureService)
        {
            this.activeFeatureService = activeFeatureService;
        }
        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("active-feature/{centerId}")]
        public async Task<IActionResult> ActiveFeature(int centerId, [FromBody] ActiveFetureRequest request)
        {
            var result = await activeFeatureService.ActiveFeaturAsync(centerId, request);
            return Ok(result);
        }
    }
}
