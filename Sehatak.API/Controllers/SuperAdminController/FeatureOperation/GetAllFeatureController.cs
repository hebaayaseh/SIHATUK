using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.Features;

namespace Sehatak.API.Controllers.SuperAdminController.FeatureOperation
{
    [ApiController]
    [Route("api/[Controller]")]
    public class GetAllFeatureController : ControllerBase
    {
        private readonly IGetAllFeature getAllFeatureService;
        public GetAllFeatureController(IGetAllFeature getAllFeatureService)
        {
            this.getAllFeatureService = getAllFeatureService;
        }
        [Authorize(Policy = "AdminOrAbove")]
        [HttpGet("get-all-feature")]
        public async Task<IActionResult> GetAllFeature()
        {
            var result = await getAllFeatureService.GetAllFeatureAsync();
            return Ok(result);
        }
    }
}
