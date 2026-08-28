using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.Centers;

namespace Sehatak.API.Controllers.SuperAdminController.Centers
{
    [ApiController]
    [Route("api/[Controller]")]
    public class GetCentersController : ControllerBase
    {
        private readonly IListOfCenters centerService;
        public GetCentersController(IListOfCenters centerService)
        {
            this.centerService = centerService;
        }

        [Authorize(Policy = "SuperAdminOnly")]
        [HttpGet("GetAllCenters")]
        public async Task<IActionResult> GetAllCenters()
        {
            var result = await centerService.GetListOfCenters();
            return Ok(result);
        }
    }
}
