using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.Application.Common;
using Sehatak.Application.Interfaces.GetSttafInterFace;

namespace Sehatak.API.Controllers.AdminController.GetStaffcontroller
{
    [ApiController]
    [Route("[Controller]")]
    public class GetSttafController : ControllerBase
    {
        private readonly IGetStaff getStaff;
        public GetSttafController(IGetStaff getStaff)
        {
            this.getStaff = getStaff;
        }
        [Authorize("AdminOnly")]
        [HttpGet("admin-get-doctors/{centerId}")]
        public async Task<IActionResult> GetDoctorsWithDepartments(int centerId, [FromQuery] PagedRequest request)
        {
            var result = await getStaff.GetDoctorsAsync(centerId,request);
            return Ok(result);
        }

        [Authorize("AdminOnly")]
        [HttpGet("admin-get-doctor/{centerId}/{doctorId}")]
        public async Task<IActionResult> GetDoctor(int centerId,int doctorId,int? year = null, int? month = null)
        {
            var result = await getStaff.GetDoctorAsync(centerId,doctorId,year,month);
            return Ok(result);
        }


    }
}
