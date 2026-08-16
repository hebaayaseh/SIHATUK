using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.GetSttafInterFace;

namespace Sehatak.API.Controllers.PatientController.GetStaffcontroller
{
    [ApiController]
    [Route("api/AdminOnly")]
    public class GetReceptionistController : ControllerBase
    {
        private readonly IGetStaff getStaff;
        public GetReceptionistController(IGetStaff getStaff)
        {
            this.getStaff = getStaff;
        }
        [Authorize(Policy = "AdminOnly")]
        [HttpGet("Get-Receptionists/{centerId}")]
        public async Task<IActionResult> GetReceptionistsAsync(int centerId)
        {
            var result = await getStaff.GetReceptionistsAsync(centerId);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpGet("Get-Receptionist/{centerId}/{userId}")]
        public async Task<IActionResult> GetReceptionistAsync(int centerId,int userId)
        {
            var result = await getStaff.GetReceptionistAsync(centerId,userId);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpGet("Get-LabTechnicians/{centerId}")]
        public async Task<IActionResult> GetLapTechnicalsAsync(int centerId)
        {
            var result = await getStaff.GetLapTechnicalsAsync(centerId);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpGet("Get-LabTechnician/{centerId}/{userId}")]
        public async Task<IActionResult> GetLapTechnicalAsync(int centerId , int userId)
        {
            var result = await getStaff.GetLapTechnicalAsync(centerId , userId);
            return Ok(result);
        }

    }
}
