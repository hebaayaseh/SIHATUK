using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.Plans;
using Sehatak.Application.Interfaces.Plans;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.SupscriptionPlans
{
    [ApiController]
    [Route("api/[Controller]")]
    public class supscriptionPlanController : ControllerBase
    {
        private readonly ISubscriptionPlan supscriptionPlan;
        public supscriptionPlanController(ISubscriptionPlan supscriptionPlan)
        {
            this.supscriptionPlan = supscriptionPlan;
        }
        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("SupscriptionPlan")]
        public async Task<IActionResult> SupscriptionPlan([FromBody] SubscriptionPlanRequestDto requst)
        {
            var result = await supscriptionPlan.AddSubscriptionPlan(requst);

            return Ok(result);
        }
    }
}
