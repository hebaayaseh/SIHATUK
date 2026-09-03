using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AssignFeaturesWithPlan;
using Sehatak.Application.DTOs.Plans;
using Sehatak.Application.DTOs.PlansDto;
using Sehatak.Application.Interfaces.Plans;
using Sehatak.Domain.Entities.SharedEntities;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.SupscriptionPlans
{
    [ApiController]
    [Route("[Controller]")]
    public class PlanController : ControllerBase
    {
        private readonly IPlan listOfPlan;
        public PlanController(IPlan listOfPlan)
        {
            this.listOfPlan = listOfPlan;
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpGet("list-of-plan")]
        public async Task<IActionResult> ListOfPlan([FromQuery] PagedRequest request)
        {
            var result = await listOfPlan.ListOfPlanAsync(request);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("superAdmin-edit-plan/{planId}")]
        public async Task<IActionResult> EditPlan(int planId, [FromBody] EditPalnRequestDto request)
        {
            var result = await listOfPlan.EditPlanAsync(planId, request);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("superAdmin-assign-feature/{planId}")]
        public async Task<IActionResult> AssignFeature(int planId, AssignFeatureToPlanRequestDto request)
        {
            var result = await listOfPlan.AssignFeatureAsync(planId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpGet("superAdminOrAdmin-get-features/{planId}")]
        public async Task<IActionResult> GetFeatures(int planId, [FromQuery] PagedRequest request)
        {
            var result = await listOfPlan.GetPlanFeaturesAsync(planId,request);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("superAdmin-supscription-plan")]
        public async Task<IActionResult> SupscriptionPlan([FromBody] SubscriptionPlanRequestDto requst)
        {
            var result = await listOfPlan.AddSubscriptionPlan(requst);

            return Ok(result);
        }
    }
}
