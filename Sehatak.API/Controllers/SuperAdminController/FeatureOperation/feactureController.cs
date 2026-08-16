using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.FeatureDto;
using Sehatak.Application.Interfaces.Features;
using Sehatak.Domain.Entities.SharedEntities;

namespace Sehatak.API.Controllers.SuperAdminController.FeatureOperation
{
    [ApiController]
    [Route("api/SuperAdminOnly")]
    public class feactureController : ControllerBase
    {
        private readonly IFeatureService featureService;
        public feactureController(IFeatureService featureService)
        {
            this.featureService = featureService;
        }
        [Authorize(Policy = "SuperAdminOnly")]
        [HttpPost("Add_Feature")]
        public async Task<IActionResult>AddFeature([FromBody] CreateFeatureRequestDto featureDto)
        {
            var result =await featureService.AddFeatureAsync(featureDto);
            
            return Ok(result);
        }

    }
}
