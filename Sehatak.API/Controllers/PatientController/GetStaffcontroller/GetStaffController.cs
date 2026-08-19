using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Interfaces.GetSttafInterFace;

namespace Sehatak.API.Controllers.PatientController.GetStaffcontroller
{
    [ApiController]
    [Route("api/AdminOnly")]
    public class GetStaffController : ControllerBase
    {
        private readonly IGetStaff getStaff;
        public GetStaffController(IGetStaff getStaff)
        {
            this.getStaff = getStaff;
        }
        
        [Authorize(Policy = "AdminOnly")]
        [HttpGet("get-staffs/{centerId}")]
        public async Task<IActionResult> GetStaffsAsync(int centerId)
        {
            var result = await getStaff.GetStaffsAsync(centerId);
            return Ok(result);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpGet("get-staff/{centerId}/{userId}")]
        public async Task<IActionResult> GetStaffAsync(int centerId , int userId)
        {
            var result = await getStaff.GetStaffAsync(centerId , userId);
            return Ok(result);
        }

    }
}
