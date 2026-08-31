using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.CentersStatusDto;
using Sehatak.Domain.Enums.SharedEnums;

namespace Sehatak.API.Controllers.SuperAdminController.CenterStatusController
{
    [ApiController]
    [Route("[Controller]")]
    public class CenterStatusController : ControllerBase
    {
        private readonly ICentersStatus centerStatusService;
        public CenterStatusController(ICentersStatus centerStatusService)
        {
            this.centerStatusService = centerStatusService;
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPatch("superAdmin-suspened-center/{centerId}")]
        public async Task<IActionResult> SuspendedCenter(int centerId)
        {
            var result = await centerStatusService.SuspendedCenter(centerId);
            return Ok(result);
        }


        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPatch("superAdmin-active-center/{centerId}")]
        public async Task<IActionResult> ActiveCenter(int centerId)
        {
            var result = await centerStatusService.ActiveCenter(centerId);
            return Ok(result);
        }

    }
}
