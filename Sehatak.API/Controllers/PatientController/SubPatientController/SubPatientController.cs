using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.SubPatientDto;
using Sehatak.Application.Interfaces.ISubPatient;
using System.Security.Claims;

namespace Sehatak.API.Controllers.PatientController.SubPatientController
{
    [ApiController]
    [Route("[Controller]")]
    public class SubPatientController : ControllerBase
    {
        private readonly ISubPatient subPatient;
        public SubPatientController(ISubPatient subPatient)
        {
            this.subPatient = subPatient;
        }
        [Authorize(Policy = "PatientOnly")]
        [HttpPost("patient-add-sub-patient/{centerId}")]
        public async Task<IActionResult> AddSubPatientAsync(int centerId, [FromBody] AddSubPatientRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await subPatient.AddSubPatientAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpPut("patient-update-sub-patient/{centerId}/{subPatientId}")]
        public async Task<IActionResult> UpdateSubPatientAsync(int centerId, int subPatientId, [FromBody] UpdateSubPatientRequestDto request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await subPatient.UpdateSubPatientAsync(centerId, userId, subPatientId, request);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpGet("patient-get-sub-patient/{centerId}")]
        public async Task<IActionResult> GetSubPatientsAsync(int centerId, [FromQuery] PagedRequest request)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var result = await subPatient.GetSubPatientsAsync(centerId, userId, request);
            return Ok(result);
        }
    }
}
