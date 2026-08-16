using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.Centers;
using Sehatak.Application.Interfaces.MedicalCenter;

namespace Sehatak.API.Controllers.SuperAdminController.Centers
{
    [ApiController]
    [Route("api/AdminOrAbove")]
    public class SpacificCenterController : ControllerBase
    {
        private readonly ISpasificCenter centerService;
        public SpacificCenterController(ISpasificCenter centerService)
        {
            this.centerService = centerService;
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpGet("GetCenterById/{centerId}")]
        public async Task<IActionResult> GetCenterById(int centerId)
        {
            var result = await centerService.GetSpasificCenterById(centerId);
            return Ok(result);
        }
    }
}
