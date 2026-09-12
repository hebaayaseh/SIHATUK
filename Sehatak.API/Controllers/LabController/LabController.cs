using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.LabDto;
using Sehatak.Application.Interfaces.ILab;

namespace Sehatak.API.Controllers.LabController
{
    [ApiController]
    [Route("[Controller]")]
    public class LabController : ControllerBase
    {
        private readonly ILab lab;
        public LabController(ILab lab)
        {
            this.lab = lab;
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpPost("doctor-create-lab-request/{centerId}")]
        public async Task<IActionResult> CreateLabRequest(int centerId, [FromBody] CreateLabRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.CreateLabRequestAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpPut("doctor-update-lab-request/{centerId}")]
        public async Task<IActionResult> UpdateLabRequest(int centerId, [FromBody] UpdateLabRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.UpdateLabRequestAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "DoctorOnly")]
        [HttpGet("doctor-get-patient-lab-requests/{centerId}")]
        public async Task<IActionResult> DoctorGetLabRequestsAsync(int centerId,int patientId, [FromQuery] PagedRequest request)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.GetLabRequestForPatientAsync(centerId, userId,patientId, request);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpGet("patient-get-patient-lab-requests/{centerId}")]
        public async Task<IActionResult> PatientGetLabRequestsAsync(int centerId,[FromQuery] PagedRequest request, [FromQuery] int?subPatientId)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var result = await lab.PatientGetLabRequestAsync(centerId, userId, request,subPatientId);
            return Ok(result);
        }

    }
}
