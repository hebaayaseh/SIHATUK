using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.AssignFeaturesWithPlan;
using Sehatak.Application.Interfaces.AssignFeatursToPlan;

namespace Sehatak.API.Controllers.SuperAdminController.FeatureOperation
{
    [ApiController]
    [Route("api/AdminOrAbove")]
    public class PlanFeatureController : ControllerBase
    {
        private readonly IPlanFeatureService planFeatureService;
        public PlanFeatureController(IPlanFeatureService planFeatureService)
        {
            this.planFeatureService = planFeatureService;
        }
        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("AssignFeature/{planId}")]
        public async Task<IActionResult> AssignFeature( int planId, AssignFeatureToPlanRequestDto request)
        {
            var result = await planFeatureService.AssignFeatureAsync(planId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpGet("GetFeatures/{planId}")]
        public async Task<IActionResult> GetFeatures(int planId)
        {
            var result = await planFeatureService.GetPlanFeaturesAsync(planId);
            return Ok(result);
        }

    }
}
