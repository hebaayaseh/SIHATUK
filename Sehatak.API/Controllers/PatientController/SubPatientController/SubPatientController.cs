using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.DTOs.SubPatientDto;
using Sehatak.Application.Interfaces.ISubPatient;

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
        [HttpPost("patient-add-sub-patient")]
        public async Task<IActionResult> AddSubPatientAsync(int centerId, int userId,[FromBody] AddSubPatientRequestDto request)
        {
            var result = await subPatient.AddSubPatientAsync(centerId, userId, request);
            return Ok(result);
        }

        [Authorize(Policy = "PatientOnly")]
        [HttpPut("patient-update-sub-patient")]
        public async Task<IActionResult> UpdateSubPatientAsync(int centerId, int userId, int subPatientId, [FromBody] UpdateSubPatientRequestDto request)
        {
            var result = await subPatient.UpdateSubPatientAsync(centerId, userId, subPatientId, request);
            return Ok(result);
        }
    }
}
