using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.CreateCenterRequestDto;
using Sehatak.Application.Interfaces.MedicalCenter;

namespace Sehatak.API.Controllers.SuperAdminController.Centers
{
    [ApiController]
    [Route("[Controller]")]
    public class CenterController : ControllerBase
    {
        private readonly ICenter centerService;
        public CenterController(ICenter centerService)
        {
            this.centerService = centerService;
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("CreateCenter")]
        public async Task<IActionResult> CreateCenter([FromForm]createCenterRequestDto request)
        {
            var result = await centerService.CreateCenterAsync(request);
            return Ok(result);
        }
        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("superAdmin-create-admin/{centerId}")]
        public async Task<IActionResult> CreateAdminToCenter(int centerId, [FromBody] CreateAdminRequestDto request)
        {
            var result = await centerService.CreateAdminAsync(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpGet("superAdmin-admin-get-center-by-Id/{centerId}")]
        public async Task<IActionResult> GetCenterById(int centerId)
        {
            var result = await centerService.GetSpasificCenterById(centerId);
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("superAdmin-get-all-centers")]
        public async Task<IActionResult> GetAllCenters()
        {
            var result = await centerService.GetListOfCenters();
            return Ok(result);
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPatch("superAdmin-suspened-center/{centerId}")]
        public async Task<IActionResult> SuspendedCenter(int centerId)
        {
            var result = await centerService.SuspendedCenter(centerId);
            return Ok(result);
        }


        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPatch("superAdmin-active-center/{centerId}")]
        public async Task<IActionResult> ActiveCenter(int centerId)
        {
            var result = await centerService.ActiveCenter(centerId);
            return Ok(result);
        }
    }
}
