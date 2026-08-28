using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.CreateCenterRequestDto;
using Sehatak.Application.Interfaces.MedicalCenter;

namespace Sehatak.API.Controllers.SuperAdminController.Centers
{
    [ApiController]
    [Route("api/[Controller]")]
    public class CreateAdminController : ControllerBase 
    {
        private readonly ICreateAdminService createAdminService;
        public CreateAdminController(ICreateAdminService createAdminService)
        {
            this.createAdminService = createAdminService;
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("create-admin/{centerId}")]
        public async Task<IActionResult> CreateAdminToCenter(int centerId ,[FromBody] CreateAdminRequestDto request)
        {
            var result = await createAdminService.CreateAdminAsync(centerId, request);
            return Ok(result);
        }
    }
}
