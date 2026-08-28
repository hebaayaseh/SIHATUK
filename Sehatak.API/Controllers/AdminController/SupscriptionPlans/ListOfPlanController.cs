using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.Plans;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.SupscriptionPlans
{
    [ApiController]
    [Route("api/[Controller]")]
    public class ListOfPlanController : ControllerBase
    {
        private readonly IListOfPlan listOfPlan;
        public ListOfPlanController(IListOfPlan listOfPlan)
        {
            this.listOfPlan = listOfPlan;
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpGet("list-of-plan")]
        public async Task<IActionResult> ListOfPlan()
        {
            var result = await listOfPlan.ListOfPlanAsync();
            return Ok(result);
        }
    }
}
