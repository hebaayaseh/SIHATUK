using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.CreateCenterRequestDto;
using Sehatak.Application.Interfaces.MedicalCenter;

namespace Sehatak.API.Controllers.SuperAdminController.Centers
{
    [ApiController]
    [Route("api/[Controller]")]
    public class CenterController : ControllerBase
    {
        private readonly ICenterService centerService;
        public CenterController(ICenterService centerService)
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
    }
}
