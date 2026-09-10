using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.EmergencyDto;
using Sehatak.Application.Interfaces.IEmengency;

namespace Sehatak.API.Controllers.EmergencyController
{
    [ApiController]
    [Route("[Controller]")]
    public class EmergencyController : ControllerBase 
    {
        private readonly IEmerngency emergency;
        public EmergencyController(IEmerngency emergency)
        {
            this.emergency = emergency;
        }

        [Authorize(Policy = "ReceptionistOnly")]
        [HttpPost("receptionist-add-patient-emergency/{centerId}")]
        public async Task<IActionResult> RegisterPatientEmergenceAsync(int centerId , [FromBody] EmergencyRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await emergency.RegisterPatientEmergencyAsync(centerId, userId, request);
            return Ok(result);
        }
    }
}
