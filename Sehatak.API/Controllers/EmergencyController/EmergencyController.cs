using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.EmergencyDto;
using Sehatak.Application.Interfaces.IEmerngency;

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

        [Authorize(Policy = "ReceptionistOnly")]
        [HttpPost("receptionist-update-patient-emergency/{centerId}")]
        public async Task<IActionResult> UpdatePatientEmergencyAsync(int centerId , [FromBody] UpdateEmergencyRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await emergency.EditPatientEmergencyAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "Emergency")]
        [HttpGet("receptionist-get-patient-emergency/{centerId}")]
        public async Task<IActionResult> GetPatientEmergencyAsync(int centerId, [FromQuery] PagedRequest request)
        {
            var result = await emergency.GetPatientEmergencyAsync(centerId, request);
            return Ok(result);
        }

        [Authorize(Policy = "ReceptionistOnly")]
        [HttpGet("receptionist-get-general-doctor/{centerId}")]
        public async Task<IActionResult> GetGeneralDoctoryAsync(int centerId, [FromQuery] PagedRequest request)
        {
            var result = await emergency.GetDoctorGeneralAsync(centerId, request);
            return Ok(result);
        }


    }
}
