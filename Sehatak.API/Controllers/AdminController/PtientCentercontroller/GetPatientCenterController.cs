using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.PatientCenter;
using Sehatak.Application.Interfaces.IPatientCenter;
using Sehatak.Domain.Enums;

namespace Sehatak.API.Controllers.SuperAdminAndAdmin.PtientCentercontroller
{
    [ApiController]
    [Route("[Controller]")]
    public class GetPatientCenterController : ControllerBase
    {
        private readonly IGetpatientCenter getpatient;
        public GetPatientCenterController(IGetpatientCenter getpatient)
        {
            this.getpatient = getpatient;
        }

        [Authorize(Policy = "AdminOrAbove")]
        [HttpPost("admin-get-patients-from-center/{centerId}")]
        public async Task<IActionResult> GetPatientsAsync(int centerId,[FromBody]AppointmentStatus status,[FromQuery] PagedRequest request)
        {
            var result = await getpatient.GetPatientesAsync(centerId,status,request);
            return Ok(result);
        }
        [Authorize(Policy = "AdminOrAbove")]
        [HttpPost("admin-get-patient-from-center/{centerId}")]
        public async Task<IActionResult> GetPatientAsync(int centerId, [FromBody] GetPatientRequestDto request)
        {
            var result = await getpatient.GetPatientAsync(centerId , request);
            return Ok(result);
        }

    }
}
