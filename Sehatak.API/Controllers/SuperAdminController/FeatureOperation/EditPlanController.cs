using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.PlansDto;
using Sehatak.Application.Interfaces.Plans;

namespace Sehatak.API.Controllers.SuperAdminController.FeatureOperation
{
    [ApiController]
    [Route("api/SuperAdminOnly")]
    public class EditPlanController : ControllerBase 
    {
        private readonly IEditPlan editPlan;
        public EditPlanController(IEditPlan editPlan)
        {
            this.editPlan = editPlan;
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("edit-plan/{planId}")]
        public async Task<IActionResult> EditPlan(int planId ,[FromBody] EditPalnRequestDto request)
        {
            var result = await editPlan.EditPlanAsync(planId, request);
            return Ok(result);
        }
    }
}
